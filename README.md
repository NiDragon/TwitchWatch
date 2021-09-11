Just a very crude simple discord bot.
# Configuration
Configuration can be handled in 3 ways.
### Developer Config
For development use `dotnet user-secrets` to manage the configuration, same format as [File based config](#file-config)
### <a name="file-config"></a>File based config
`config.json` can be used to configure the application,
```json
{
  "Discord":{
    "Token": ""
  },
  "Twitch": {
    "ClientID": "",
    "ClientSecret": ""
  },
  "Prefix": "",
  "ListenChannel":"",
  "EchoChannel": "",
  "RunOnStart": ""
}
```
### Environment Variables
- `Discord:Token`
- `Twitch:ClientId`
- `Twitch:ClientSecret`
- `Prefix`
- `ListenChannel`
- `EchoChannel`
- `RunOnStart`