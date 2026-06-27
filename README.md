# rabbitmq-demo

A .NET 10 demo application using RabbitMQ for message brokering with an API, email/SMS workers, and a React UI.

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) (for the UI)

## Run Locally

### 1. Start RabbitMQ

```bash
docker compose up -d
```

RabbitMQ management UI will be available at `http://localhost:15672` (user: `user`, password: `password`).

### 2. Run the API

```bash
cd app/api/src
dotnet run
```

The API starts at `http://localhost:5139`.

### 3. Run the Workers

In separate terminals:

```bash
# Email worker
cd app/email-worker/src
dotnet run
```

```bash
# SMS worker
cd app/sms-worker/src
dotnet run
```

### 4. Run the UI

```bash
cd app/ui
npm install
npm run dev
```

The UI starts at `http://localhost:5173`. Open it in your browser, fill in the form, and submit a message.

### Verify

Look at the console logs of the API and workers to confirm messages are being published and processed.