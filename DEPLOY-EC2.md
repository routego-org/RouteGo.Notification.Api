# Deploy Notification API to AWS EC2 — Step by Step

Simple guide to run this app on EC2 with 2 containers (Postgres + this app),
then send a test SQS event and receive a verification email.

## My AWS setup (already created)

```
AWS_REGION        = ap-south-1 (Mumbai)
Account           = 675252699468
VPC               = vpc-0ea392917d694430f
Internet Gateway  = igw-0b463e098af052be4
Subnet            = subnet-0eb19c4b526bd0a93
Route Table       = rtb-044add6e155258c11
Security Group    = sg-0cb306b73a27bc358

SQS queue         = https://sqs.ap-south-1.amazonaws.com/675252699468/notification-q
SQS DLQ           = https://sqs.ap-south-1.amazonaws.com/675252699468/notification-dlq
Secret            = routego/notification/db
Secret (key)      = routego/internal-key   (shared with the Web Portal)
IAM role          = routego-dev-ec2-role (instance profile with same name)
EC2               = i-097da83235e37d960   public IP 13.127.229.166
```

---

## Quick way — one command deploy (use this after Part A is done)

Parts C, D and E below are done **automatically** by one script, like a small pipeline.

**One time only, on your PC:**
1. Copy `deploy\ec2\.env.example` to `deploy\ec2\.env` and fill it in (see Part E for the values).
   This file is ignored by git, so your password is not committed.
2. Open `deploy\ec2\deploy.ps1` and check the 3 values at the top:
   `HostIp`, `KeyPath` (path of your `.pem` file), `User` (`ec2-user` or `ubuntu`).

**Every time you want to deploy (PowerShell):**
```powershell
cd D:\Project-learn-aws\02-Code-Projects\RouteGo.Notification.Api
.\deploy\ec2\deploy.ps1
```
If PowerShell says "running scripts is disabled", run this once:
`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`

What the script does:

| Step | What happens |
|---|---|
| 1/4 | Packs your code into one file (no bin/obj/.git) |
| 2/4 | Copies the code + your `.env` to the EC2 |
| 3/4 | Installs Docker, Compose, swap — **only the first time** (`setup-server.sh`) |
| 4/4 | Builds the image, starts containers, waits for `/health` (`start.sh`) |

At the end you see `DEPLOY OK`. If a step fails, it stops and shows which step failed.

### When I stop and start the EC2 — what do I do?

**Nothing.** Docker starts itself when the server boots, and the containers have
`restart: unless-stopped`, so they come back on their own.

Only one thing changes: the **public IP** (if you have no Elastic IP). Find the new IP in
EC2 → Instances → *Public IPv4 address*, then:
```powershell
.\deploy\ec2\deploy.ps1 -HostIp 3.110.45.67      # example new IP
```
(Or create an Elastic IP once so the IP never changes.)

### Files of the pipeline

| File | Runs on | Job |
|---|---|---|
| `deploy/ec2/deploy.ps1` | Your PC | The one command — runs all steps |
| `deploy/ec2/setup-server.sh` | EC2 | Installs Docker, Compose, swap (skips if already done) |
| `deploy/ec2/start.sh` | EC2 | Build image, start containers, health check |
| `deploy/ec2/docker-compose.yml` | EC2 | Defines Postgres + migrate helper + app |
| `deploy/ec2/.env` | PC → EC2 | Your passwords and settings (never committed) |

Notes:
- `POSTGRES_PASSWORD` is used **only the first time** the database is created. If you change it later,
  also change it in the secret **and** inside the database. Choose it once and keep it.
- Database data is kept in a Docker volume, so deploying again does **not** delete your data.

The manual steps below (Parts C, D, E) still work. Read them to understand what the script does.

---

## Part A — What is left in AWS

| # | Item | Status | Why the app needs it |
|---|---|---|---|
| 1 | SES email verification | **MISSING** | App sends email with Amazon SES. SES only sends from a verified address. |
| 2 | Container "hop limit" = 2 | **MISSING** (usually) | Without it, the app inside Docker cannot use the EC2 role. |
| 3 | Role permissions | CHECK | App must read the secret, read the queue, send email. |
| 4 | Queue linked to DLQ | CHECK | App **stops at startup** if `notification-q` has no dead-letter queue. |
| 5 | Secret value | CHECK | App reads the database login from this secret. |
| 5b | Internal key secret | **MISSING** | App **stops at startup** without it. The Web Portal sends the same key to search the send log and resend emails. |
| 6 | Security group rules | CHECK | Port 22 must be open so you can connect. |
| 7 | Key file (`.pem`) | CHECK | This is your "password" to connect to EC2. |

### A1. SES — verify your email (MISSING)
1. AWS Console → top right, select region **Mumbai (ap-south-1)** → search **"SES"**.
2. Left menu **Identities** → **Create identity** → choose **Email address** → enter your email
   (example: `vikas.test@gmail.com`) → **Create**.
3. Open your mailbox and click the link from AWS.

Why: a new SES account is in "sandbox" mode. In sandbox, SES sends **only from and to verified emails**.
For testing, use the same verified email as sender and receiver.

### A2. Hop limit = 2 (MISSING)
EC2 → **Instances** → select `i-097da83235e37d960` → **Actions** → **Instance settings** →
**Modify instance metadata options** → **Metadata response hop limit = 2** → **Save**.

Why: Docker adds one extra network "hop". With value 1, you get the error
*"Unable to get IAM security credentials"*.

### A3. Role permissions (CHECK)
- **Is the role attached to EC2?** EC2 → your instance → **Security** tab → **IAM Role** must show
  `routego-dev-ec2-role`. If empty: **Actions** → **Security** → **Modify IAM role** → choose it.
- **Does the role have permissions?** IAM → **Roles** → `routego-dev-ec2-role` → **Add permissions** →
  **Create inline policy** → **JSON** → paste below → name it `routego-notification`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    { "Effect": "Allow", "Action": "secretsmanager:GetSecretValue",
      "Resource": [
        "arn:aws:secretsmanager:ap-south-1:675252699468:secret:routego/notification/db-*",
        "arn:aws:secretsmanager:ap-south-1:675252699468:secret:routego/internal-key-*"
      ] },
    { "Effect": "Allow", "Action": ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes"],
      "Resource": "arn:aws:sqs:ap-south-1:675252699468:notification-q" },
    { "Effect": "Allow", "Action": ["ses:SendEmail", "ses:SendRawEmail"], "Resource": "*" }
  ]
}
```

### A4. Queue → DLQ link (CHECK)
SQS → `notification-q` → **Edit** → **Dead-letter queue** → **Enabled** → choose `notification-dlq` →
**Maximum receives = 5** → **Save**.

Why: if an email fails 5 times, the message moves to the DLQ instead of repeating forever.
The app checks this link when it starts.

### A5. Secret value (CHECK)
Secrets Manager → `routego/notification/db` → **Retrieve secret value** → **Edit** → **Plaintext**.
It must look exactly like this:

```json
{"host":"postgres","port":5432,"database":"routego_notification","username":"notification_user","password":"MyStrongPass123","sslmode":"disable"}
```

- `host` must be **`postgres`** (not an IP). Containers find each other by name.
- Remember the `password` — you will write the **same password** on the server later.

### A5b. Internal key secret (MISSING)
Secrets Manager → **Store a new secret** → **Other type of secret** → **Plaintext** tab → delete the
`{}` and paste only the key, a long random value (example: the output of
`openssl rand -base64 32`) → **Next** → name: **`routego/internal-key`** → **Next** → **Store**.

- Plain text, **not** JSON — the whole value is the key.
- The Web Portal must send this same value in the `X-Internal-Key` header. Keep it nowhere else.
- The role policy in A3 already allows reading it.

### A6. Security group (CHECK)
EC2 → **Security Groups** → `sg-0cb306b73a27bc358` → **Edit inbound rules**:
- **SSH**, port 22, source **My IP** (required).
- **Custom TCP**, port 5007, source **My IP** (optional — to open `/health` in your browser).
- **Never** open port 5432 (database) to the internet.

### A7. Key file (CHECK)
When you created the EC2, AWS asked for a **key pair** and you downloaded a file like
`routego-key.pem`. Find it (example: `C:\Users\YourName\Downloads\routego-key.pem`).

*Optional:* **Elastic IP**. Your IP `13.127.229.166` **changes** if you stop and start the EC2.
An Elastic IP keeps it fixed.

---

## Part B — Connect to the EC2

Open **PowerShell** on your PC.

**Step 1 — fix key file permission** (one time only; otherwise SSH refuses the key):
```powershell
$key = "C:\Users\YourName\Downloads\routego-key.pem"
icacls.exe $key /reset
icacls.exe $key /grant:r "$($env:USERNAME):(R)"
icacls.exe $key /inheritance:r
```

**Step 2 — connect:**
```powershell
ssh -i $key ec2-user@13.127.229.166
```
- Type `yes` the first time.
- If it says "Permission denied", your server is probably Ubuntu → use `ubuntu@` instead of `ec2-user@`.
- Now your prompt looks like `[ec2-user@ip-... ~]$` — you are inside the server.
- Type `exit` to leave.

> Note: in a new PowerShell window, run the `$key = "..."` line again before `ssh` / `scp`.

---

## Part C — Install Docker on the server (one time)

Run on the server (Amazon Linux):

```bash
sudo dnf install -y docker
sudo systemctl enable --now docker
sudo usermod -aG docker ec2-user

# Docker Compose plugin
sudo mkdir -p /usr/local/lib/docker/cli-plugins
sudo curl -SL https://github.com/docker/compose/releases/latest/download/docker-compose-linux-x86_64 -o /usr/local/lib/docker/cli-plugins/docker-compose
sudo chmod +x /usr/local/lib/docker/cli-plugins/docker-compose

# 2 GB extra memory (swap), so the .NET build does not crash on a small server
sudo dd if=/dev/zero of=/swapfile bs=1M count=2048
sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile swap swap defaults 0 0' | sudo tee -a /etc/fstab

exit
```
Connect again (Part B). Check: `docker compose version` should print a version.

(Ubuntu instead: `sudo apt update && sudo apt install -y docker.io docker-compose-v2` replaces the first two blocks.)

---

## Part D — Copy your code to the server

On your **PC** (PowerShell):
```powershell
cd D:\Project-learn-aws\02-Code-Projects\RouteGo.Notification.Api
tar -czf $env:TEMP\notification.tar.gz --exclude=bin --exclude=obj --exclude=.git --exclude=.vs .
scp -i $key $env:TEMP\notification.tar.gz ec2-user@13.127.229.166:~
```

On the **server**:
```bash
mkdir -p ~/notification && tar -xzf ~/notification.tar.gz -C ~/notification
```

---

## Part E — Start the two containers

On the **server**:

```bash
cd ~/notification/deploy/ec2
cp .env.example .env
nano .env
```
Fill it like this example:
```
POSTGRES_PASSWORD=MyStrongPass123                 <- same password as in the secret (A5)
NOTIFICATION_QUEUE_URL=https://sqs.ap-south-1.amazonaws.com/675252699468/notification-q
SENDER_ADDRESS=vikas.test@gmail.com               <- the email you verified in SES (A1)
PORTAL_BASE_URL=http://localhost:8080/
```
Save in nano: **Ctrl+O**, **Enter**, then **Ctrl+X**.

Build and start:
```bash
cd ~/notification
docker build -t routego/notification:latest .      # 5–10 minutes the first time
cd deploy/ec2
docker compose up -d
docker compose ps -a
```
You should see:
- `postgres` → **running (healthy)**
- `migrate` → **exited (0)** — this is correct. It is a small helper that creates the tables once, then stops.
- `notification` → **running**

Check the app is alive:
```bash
curl http://localhost:5007/health        # prints: healthy
docker compose logs -f notification      # live logs (Ctrl+C to stop watching)
```

---

## Part F — Send a test event and get the email

1. AWS Console → **SQS** → `notification-q` → **Send and receive messages**.
2. Paste this in **Message body** (change `email` to your verified email):
```json
{
  "eventId": "evt-test-0001",
  "eventType": "UserRegistered",
  "version": 1,
  "occurredAt": "2026-10-05T10:00:00Z",
  "correlationId": "corr-test-0001",
  "producer": "manual-test",
  "data": {
    "userId": "usr-1",
    "email": "vikas.test@gmail.com",
    "name": "Vikas",
    "role": "Customer",
    "verificationLink": "https://example.com/verify?token=abc123"
  }
}
```
3. Click **Send message** → check your inbox (**and spam folder**).

Important:
- **Change `eventId` for every test** (`evt-test-0002`, `evt-test-0003`, ...). The app sends only
  **one email per eventId** — the same eventId again sends nothing.
- `eventType` must be exactly `UserRegistered` (capital U and R).
- `role` can be `Customer` or `Operator`.

See what the app saved in the database:
```bash
docker compose exec postgres psql -U notification_user -d routego_notification \
  -c "select event_id, email_type, status, failure_reason from notification.send_log_entries order by attempted_at desc limit 5;"
```
`status = sent` means the email was sent.

Later, the Identity app will send this same JSON to `notification-q`. No change is needed in this app.

---

## Part G — If something goes wrong

| What you see in `docker compose logs notification` | Fix |
|---|---|
| `Unable to get IAM security credentials` | Hop limit (A2), or role not attached (A3) |
| `AccessDenied` on secretsmanager / sqs / ses | Role policy (A3) |
| Error about `RedrivePolicy` | Queue → DLQ link (A4) |
| `password authentication failed` | Password in secret ≠ `POSTGRES_PASSWORD` in `.env` |
| `Email address is not verified` | SES verification (A1) |
| Message went to `notification-dlq` | JSON is wrong (for example, `eventType` misspelled) |

---

## Update the app after a code change

Easy way: run `.\deploy\ec2\deploy.ps1` again. That's all.

Manual way:
1. Repeat **Part D** (copy code again).
2. On the server:
```bash
cd ~/notification
docker build -t routego/notification:latest .
cd deploy/ec2
docker compose up -d
```
Your database data stays safe (it is stored in a Docker volume).

## Useful commands (on the server, inside `~/notification/deploy/ec2`)

| Command | What it does |
|---|---|
| `docker compose ps -a` | Show containers and their status |
| `docker compose logs -f notification` | Live app logs |
| `docker compose restart notification` | Restart only the app |
| `docker compose down` | Stop everything (data is kept) |
| `docker compose up -d` | Start everything again |

## Before deploying the v2 emails

The v2 emails add three things the app needs before it will start:

1. **A second queue for bounce and complaint reports**, for example `email-service-reports`, with a
   dead-letter queue and a redrive policy exactly like `notification-q` (the app stops at startup if
   its redrive policy is missing). Point Amazon SES event publishing (bounces and complaints) at it,
   through an SNS topic. Put its URL in `.env` as `EMAIL_SERVICE_REPORTS_QUEUE_URL`; the app refuses to
   start without it. Give the app's role `sqs:ReceiveMessage`, `sqs:DeleteMessage` and
   `sqs:GetQueueAttributes` on it, as for `notification-q`, and add an alarm on its dead-letter queue.
2. **New database tables.** The migration step (`migrate` in the compose file) applies
   `V0005` and `V0006`, which add the known bookings and the do-not-send list. Nothing else to do.
3. **Optional setting** `ReminderLeadHours` (default 24): how long before departure the departure
   reminder is sent. Departure reminders are fired by a timer inside the app, once a minute.

## Files used by this guide

- `deploy/ec2/docker-compose.yml` — Postgres + migration helper + app, using real AWS.
- `deploy/ec2/.env.example` — copy to `.env` on the server and fill in.
