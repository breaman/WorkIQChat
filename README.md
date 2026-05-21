## Overview
This application sets up a simple chat agent that connects to the WorkIQ MCP Server. This is not an example of how to write a production ready application, this is just another example of how to talk with the WorkIQ MCP server but using blazor instead of a console application. The WorkIQ MCP Server is hosted via an npx command, so this is no means a "server ready" application. At the time of this project creation, there was not a way to connect to the WorkIQ MCP Server via an Http Transport (only STDIO was available). When the Http Transport is supported, it is highly recommended to use that model and configure authentication correctly.

## Known Issues / Stumbling Blocks
- This application uses a STDIO version of the WorkIQ MCP Server. Inside the code it spawns the WorkIQ MCP Server using the following command `workiqArgs = ["-y", "@microsoft/workiq@0.4.1", "mcp"];`. It is pinned at the 0.4.1 release because when this application was written, the `@latest` release had a bug when running on my mac.
- When run the first time, if you are not authenticated to the WorkIQ MCP Server, a window should pop up and prompt you for credentials. This worked great on my mac, but on my windows machine I ran into the following issue [WorkIQ WAM Issue](https://github.com/microsoft/work-iq/issues/118) and so I had to follow the directions in the answer that suggested to run the following command so that WAM was not used `npx -y @microsoft/workiq@0.4.1 config set disableBrokeredAuth=true`

## Bootstrap
Since this template utilizes bootstrap scss, the initial css file needs to be generated. There are two scripts included for doing this, one is sass-dev that will run the process in watch mode and the other is sass-prod that will compress the css file for production use. In order to do this perform the following steps in your terminal:

```
cd src/WorkIQChat.Server
npm run sass-dev (or sass-prod depending on which one you want)
```

## EF Migrations
This project adds EF as a dotnet tool, so before running any EF commands, one needs to run the following command from the project folder (there is also a command in the Aspire dashboard to run this restore command if the app is started before the restore command is run manually):

```
dotnet tool restore
```

After creating a new project with this template, migrations need to be run since there is some authentication that has been added. To do this, run the following command in your terminal:

```
cd src/WorkIQChat.Server
dotnet ef migrations add InitialDatabase -p ../WorkIQChat.Data
```

Migrations will run when the aspire project is started, so no need to run the migration manually after it is created.

## Aspire
This project is configured with aspire, so the recommended way to kick off project execution is the following:

```
cd aspire/WorkIQChat.AppHost
dotnet watch
```
