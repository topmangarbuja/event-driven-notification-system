// Aspire TypeScript AppHost
// For more information, see: https://aspire.dev

import { createBuilder, EndpointProperty } from './.aspire/modules/aspire.mjs';

const builder = await createBuilder();

const rabbitUser = 'user';
const rabbitPassword = 'password';

// The UI's public port is pinned here so every developer opens the same URL. Aspire also
// allocates Vite's own listening port and injects it as VITE_PORT, so app/ui/vite.config.ts
// does not need to repeat any port. Override the public port (e.g. UI_PORT=5273) to run a
// second stack alongside this one.
const ports = {
    ui: Number(process.env.UI_PORT ?? 5173),
};

// Broker. The amqp endpoint is referenced by every .NET resource below, which also
// makes them wait until the broker reports healthy before starting.
const rabbitmq = await builder
    .addContainer('rabbitmq', 'rabbitmq:4.1.4-management')
    .withEnvironment('RABBITMQ_DEFAULT_USER', rabbitUser)
    .withEnvironment('RABBITMQ_DEFAULT_PASS', rabbitPassword)
    .withEndpoint({ name: 'amqp', targetPort: 5672 })
    .withHttpEndpoint({ name: 'management', targetPort: 15672 })
    .withHttpHealthCheck({ path: '/', endpointName: 'management' });

const amqp = await rabbitmq.getEndpoint('amqp');

// The API publishes to the fanout exchange. No endpoint is declared here: Aspire infers one
// named "http" from the launch profile's applicationUrl in
// app/api/src/Properties/launchSettings.json and keeps it in sync with the port it actually
// allocates, which the dashboard shows. Declaring a same-named endpoint shadows that inferred
// one and leaves the dashboard advertising a port the app is not listening on.
const api = await builder
    .addCSharpApp('api', '../app/api/src/src.csproj')
    .waitFor(rabbitmq)
    .withEnvironment('RABBITMQ_HOST', amqp.property(EndpointProperty.Host))
    .withEnvironment('RABBITMQ_PORT', amqp.property(EndpointProperty.Port))
    .withEnvironment('RABBITMQ_USER', rabbitUser)
    .withEnvironment('RABBITMQ_PASS', rabbitPassword);

const apiHttp = await api.getEndpoint('http');

await builder
    .addCSharpApp('email-worker', '../app/email-worker/src/src.csproj')
    .waitFor(rabbitmq)
    .withEnvironment('RABBITMQ_HOST', amqp.property(EndpointProperty.Host))
    .withEnvironment('RABBITMQ_PORT', amqp.property(EndpointProperty.Port))
    .withEnvironment('RABBITMQ_USER', rabbitUser)
    .withEnvironment('RABBITMQ_PASS', rabbitPassword);

await builder
    .addCSharpApp('sms-worker', '../app/sms-worker/src/src.csproj')
    .waitFor(rabbitmq)
    .withEnvironment('RABBITMQ_HOST', amqp.property(EndpointProperty.Host))
    .withEnvironment('RABBITMQ_PORT', amqp.property(EndpointProperty.Port))
    .withEnvironment('RABBITMQ_USER', rabbitUser)
    .withEnvironment('RABBITMQ_PASS', rabbitPassword);

// Vite dev server. It is given no targetPort: Aspire picks one and passes it in as VITE_PORT,
// so the only port anyone declares is the public one above. API_URL is read by the Vite config
// to point the /api proxy at the API's allocated endpoint.
await builder
    .addExecutable('ui', 'pnpm', '../app/ui', ['run', 'dev'])
    .withHttpEndpoint({ port: ports.ui, env: 'VITE_PORT' })
    .withReference(api)
    .withEnvironment('API_URL', apiHttp)
    .waitFor(api);

await builder.build().run();