// Slice test files (linked in from {Project}/Features/**/*.Tests.cs) carry no using directives —
// this file holds the test project's shared set. The sibling slice skill EXTENDS it as its
// test helpers need more (e.g. Testcontainers, AngleSharp).
global using Xunit;
global using Notification.Common;             // HttpClients, WireJson
// Dapper, Npgsql, System.Text.Json and Infrastructure.Pgsql stay out of this list: the database
// fixture files declare them themselves, so a global copy here turns theirs into IDE0005 errors.
// Slice tests reach the database and build events through the helpers in Support/ instead.
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Testing;   // FakeLogger
global using Notification.Common.Email;           // EmailSendRecorder, in EmailTestHost
global using Notification.Common.KnownBookings;   // KnownBookingStore, in EmailTestHost
global using Notification.Common.Ports;
global using Notification.Domain;
global using Notification.Tests;              // ApiFactory, for the internal API tests
global using Notification.Tests.Database;
global using Notification.Tests.Support;
global using System.Net;                     // HttpStatusCode in the internal API tests
