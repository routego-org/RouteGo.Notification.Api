#!/bin/bash
# LocalStack init (LOCAL ONLY): runs inside the LocalStack container when it is ready.
# Creates the Notification queues with their DLQs and verifies the local sender identity.
# Real AWS queues are created once per account by hand / infra (02-notification.md §6 step 4).
set -euo pipefail

REGION="ap-south-1"
MAX_RECEIVE_COUNT=5   # after 5 attempts a message goes to the DLQ (02-notification.md §3.1 step 6)

create_queue_with_dlq() {
  local name="$1"
  local dlq_name="${name%-q}-dlq"

  awslocal sqs create-queue --region "$REGION" --queue-name "$dlq_name" >/dev/null
  local dlq_url dlq_arn
  dlq_url=$(awslocal sqs get-queue-url --region "$REGION" --queue-name "$dlq_name" --query QueueUrl --output text)
  dlq_arn=$(awslocal sqs get-queue-attributes --region "$REGION" --queue-url "$dlq_url" \
    --attribute-names QueueArn --query Attributes.QueueArn --output text)

  awslocal sqs create-queue --region "$REGION" --queue-name "$name" \
    --attributes "{\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"${dlq_arn}\\\",\\\"maxReceiveCount\\\":\\\"${MAX_RECEIVE_COUNT}\\\"}\"}" \
    >/dev/null
  echo "created $name (DLQ: $dlq_name)"
}

# notification-q -> notification-dlq (the DLQ the CloudWatch alarm watches in AWS)
create_queue_with_dlq notification-q
create_queue_with_dlq notification-reminders-q
create_queue_with_dlq notification-ses-events-q

awslocal ses verify-email-identity --region "$REGION" --email-address noreply@routego.local
echo "verified sender noreply@routego.local"
