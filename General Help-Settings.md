# TBot
## General Help for the Bot settings.json file

This is a general guide to deal with setting up TBot. In case you need specific help, please drop us a line at the Discord server:

[![Stories in Ready](https://discordapp.com/api/guilds/801453618770214923/widget.png?style=banner2)](https://discord.gg/NZSaY4aQ7J)

## Disclaimer

Scripting and botting are forbidden by the OGame rules.
I adopt a series of measures to prevent detection and thus ban, but I cannot, and never will, guarantee anything.
Use this bot at your own risk!!

Testing and PR are very much appreciated!

## Help by Sections
### Login

    "Credentials": {
		"Universe": "",
		"Email": "",
		"Password": "",
		"Language": "",
		"LobbyPioneers": false,
		"BasicAuth": {
			"Username": "",
			"Password": ""


In the credentials part, the information in simple to understand. Universe is the name of you universe with Initial Capital Letter(ex. ___Myuniverse___ ). Email is the address you use to login to Ogame, as well as the password. The Language setting is the two letters which are the language setting of you universe in non capital letters (ex. ***en, es, it, fr, ar***, etcetera), which is also the country of the server. The LobbyPioneers is for the Pioneers loby only, set it to  ***true*** and use your credentials in the Username and Password fields.

### Connection

    "General": {
        "UserAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.110 Safari/537.36",
        "Host": "localhost",
        "Port": "8080",

In this section,  the Host setting is used so you can access the game in the broswer and play. for example, if you leave localhost, you'll access the game in the address ***http://localhost:8080/game/index.php*** , but it'll only be accessible from your computer. if you set it to your computer's local network IP address, (ex. 192.168.1.10), you can access it at  ***http://192.168.1.10:8080/game/index.php*** and so on. As for the port, you can use any port you want, just try not to set it to anything another program is using.

# **NEED TO WRITE USER AGENT INSTRUCTIONS**
#### Proxy Settings and else for connection:

    "Proxy": {
    "Enabled": false,
    "Address": "",
    "Type": "socks5",
    "Username": "",
    "Password": "",
    "LoginOnly": true
    },
    "CaptchaAPIKey": "",
    "CustomTitle": "",
    "SlotsToLeaveFree": 1

Enabled refers to as where you want to use a proxy or not. If you want it set to ***true***. Address is the Proxy's IP address, and type can be for example ***socks4, socks5***, etc, depending on your proxy. Also you can write here the username and password to login to your proxy service. If LoginOnly is set to true, TBot only uses it to login to the game.

TBot, being based on ogamed, supports manual captcha solving as well as Ninja Captcha Autoresolve service. To manually solve captcha navigate to ***http://host:port/bot/captcha*** . To configure Ninja Capthca Service follow [this guide](https://github.com/alaingilbert/ogame/wiki/auto-captcha-using-ninja-solver) and insert the obtained APIKey in settings.json .

CustomTitle will modify the titlebar of the command prompt or shell window where TBot is running, which helps ID the account it is working on.
SlotsToLeaveFree refers to how many slots will missions sent by TBot leave free for playing or contingency, so as to not take all spaces up. This setting is overridden when being attacked, so be careful to procure many free slots in the game via the Store or increasing computing research.  
