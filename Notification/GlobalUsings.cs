// Slice files carry no using directives — this file holds the shared set.
// The sibling slice skill and database setup EXTEND this list beneath the '// FILLED BY' marker below.
global using System.Text.Json;          // JsonSerializer, WireJson.Options
global using Microsoft.AspNetCore.Mvc;
global using Notification.Common;             // HttpClients, WireJson
global using Notification.Common.Ports;       // IClock
global using Notification.Common.Problems;    // ProblemResult, ProblemTypes (catalog projection)
global using Notification.Common.Behaviors;   // UnhandledExceptionHandler, ResponseMetadataMiddleware, ResponseHeadersTransformer
// FILLED BY the sibling slice skill — and by skills-db-postgres (copy-assets.ps1) at database setup:
global using System.Data.Common;   // skills-db-postgres (copy-assets.ps1)
global using Dapper;   // skills-db-postgres (copy-assets.ps1)
global using Notification.Domain;             // SendLogRules, EmailMessageBuilder, ReceivedEvent, the send log enumerations
global using FluentValidation;                // the internal support operations' request validators
global using FluentValidation.Results;        // ValidationResult in the controller
global using Microsoft.AspNetCore.Authorization;   // [Authorize] on the internal support operations
global using Notification.Common.Email;           // EmailSendRecorder
global using Notification.Common.KnownBookings;   // KnownBookingStore
