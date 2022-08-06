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


In the `Credentials` part, the information in simple to understand. `Universe` is the name of you universe with Initial Capital Letter(ex. ___Myuniverse___ ). `Email` is the address you use to login to Ogame, as well as the `Password`. The `Language` setting is the two letters which are the language setting of you universe in non capital letters (ex. ***en, es, it, fr, ar***, etcetera), which is also the country of the server. The `LobbyPioneers` is for the Pioneers Lobby only, set it to  ***true*** and use your credentials in the `Username` and `Password` fields.

### Connection

    "General": {
        "UserAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.110 Safari/537.36",
        "Host": "localhost",
        "Port": "8080",

In this section,  the `Host` setting is used so you can access the game in the broswer and play. for example, if you leave localhost, you'll access the game in the address ***http://localhost:8080/game/index.php*** , but it'll only be accessible from your computer. if you set it to your computer's local network IP address, (ex. 192.168.1.10), you can access it at  ***http://192.168.1.10:8080/game/index.php*** and so on. As for the `Port`, you can use any port you want, just try not to set it to anything another program is using.

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

`Enabled` refers to as where you want to use a proxy or not. If you want it set to ***true***. `Address` is the Proxy's IP address, and type can be for example ***socks4, socks5***, etc, depending on your proxy. Also you can write here the `Username` and `Password` to login to your proxy service. If `LoginOnly` is set to true, TBot only uses it to login to the game.

TBot, being based on Ogamed, supports manual captcha solving as well as Ninja Captcha Autoresolve service. To manually solve captcha navigate to ***http://host:port/bot/captcha*** . To configure Ninja Capthca Service follow [this guide](https://github.com/alaingilbert/ogame/wiki/auto-captcha-using-ninja-solver) and insert the obtained APIKey in the `CaptchaAPIKey` field.

`CustomTitle` will modify the titlebar of the command prompt or shell window where TBot is running, which helps ID the account it is working on.
`SlotsToLeaveFree` refers to how many slots will missions sent by TBot leave free for playing or contingency, so as to not take all spaces up. This setting is overridden when being attacked, so be careful to procure many free slots in the game via the Store or increasing computing research.

#### SleepMode

    "SleepMode": {
      "Active": false,
      "GoToSleep": "23:15",
      "WakeUp": "07:05",
      "PreventIfThereAreFleets": true,
      "TelegramMessenger": {
      "Active": false
      },
      "AutoFleetSave": {
        "Active": true,
        "OnlyMoons": true,
        "ForceUnsafe": true,
        "DeutToLeave": 200000,
        "Recall": true
      }

Sleep Mode settings go as follows:
`Active` must be set to true or false. If true, TBot will Turn itself off completely at the set time, and it takes into account SERVER TIME (your computer or server), TBot will then  wake up at the  time set in the `WakeUp` line (notice 24 hr format). If `PreventIfThereAreFleets` is set to true, TBot will wait for fleets to return before going into sleep mode, else it will not wait. The `TelegramMessenger` setting here lets you know via message that TBot is about to go to sleep. Sleep mode means the program does not interact with Ogame servers, so take into account that all other missions and even the defender setting will be overlooked during sleep.

As for the `AutoFleetSave` function, if set to true, TBot will make your fleet fly before entering SleepMode, if  `OnlyMoons` set to true, it will only make fleets in moons fly, and `ForceUnsafe` means when set to true that it will use missions that are not DEPLOY in order to do that fleetsave mission. `DeutToLeave` is in every place that fleets are taking off from, and if `Recall` is set to true, TBot will make sure the fleet is recalled in time for it to be home at the `WakeUp` time.

#### Defender Settings

    "Defender": {
      "Active": true,
      "CheckIntervalMin": 1,
      "CheckIntervalMax": 22,
      "IgnoreProbes": true,
      "IgnoreWeakAttack": true,
      "WeakAttackRatio": 3,
      "Autofleet": {
        "Active": true,
        "TelegramMessenger": {
          "Active": false
        }
      },
      "WhiteList": [
        100000,
        100003,
        100004
      ],

The Defender Settings begin with the `Active` setting, which is set to true or false. If it is set to true, TBot will check for attacks at a random time set between the following options `CheckIntervalMin` and `CheckIntervalMax`, which are set in minutes. If `IgnoreProbes` is set to true, TBot will do nothing if attacked by probes (not spying missions, just probes in the attacking fleet). ___Defender will also ignore `WeakAttacks`, when the next setting is set to true, and the `WeakAttackRatio` setting works as follows: if it is set to for example 2, it will **NOT** ignore if attacking fleet is over 1/2 of your fleet, or if set to 3, it will **NOT** ignore if attacking fleet is over 1/3 of your fleet. If say you want it to ignore everything except a fleet as big as yours, you can set it to 1 or if you want it to ignore fleets less than double the size of your fleet, you can set it to  0.5.___

The actions TBot does once an attack is being received and is **not** ignored begins with the `Autofleet` setting, which when set to true will send you a message if specified to do so in the `TelegramMessenger` and it will let you know the player attacking, where the attack is being sent to, and if your espionage tech is high enough, the composition of the attacking fleet. This setting can be set to true or false. Also, TBot will then schedule and send your fleet to another place in order to save it from the attack. The `WhiteList` setting, tells TBot which players (by their Ogame ID) are allowed to attack your account even when Defender is active.


      "SpyAttacker": {
        "Active": true,
        "Probes": 20
      },
      "MessageAttacker": {
        "Active": true,
        "Messages": [
          "hey",
          "hello",
          "Tripa tripaloski",
          "Tartu!"
        ]
      },
      "TelegramMessenger": {
        "Active": false
      },
      "Alarm": {
        "Active": true
      }

Other actions by the Defender settings are `SpyAttacker`, `MessageAttacker` and `TelegramMessenger`  which are self explanatory,  and `Alarm`, which if set to true, will play a loud sound in your computer (useless if TBot is in a remote server) .


#### Brain
This section controls most of the actions in TBot, specially when it comes to building things and researching.

      "Brain": {
        "Active": true,

The above setting turn all of the Brain functions on or off.

        "AutoMine": {
			"Active": true,
This section turn on or off the AutoMine actions, which include settings for the maximum levels for all the buildings you want on your planets/ moons. For example, the line:

      "MaxMetalMine": 40,
      "MaxCrystalMine": 35,
means the max metal mine to be built by the brain is a level 40 mine, etceteras.

The Transports section sets a kind of ship and a planet of origin from which resources will be sent to other planets. The `DeutToLeave` setting is the ammount of Deuterium to be left in the origin coordinates. If you set the `"Exclude":` to some coordinates, it will not ship resources or build things in those coordinates.

The following lines:

      "OptimizeForStart": true,
      "PrioritizeRobotsAndNanites": false,
      "BuildDepositIfFull": false,
      "DepositHours": 6,
      "MaxDaysOfInvestmentReturn": 365,
      "DeutToLeaveOnMoons": 1000000,
      "CheckIntervalMin": 10,
      "CheckIntervalMax": 20
are especially useful in the beginning, when your priorities in terms of buildings have to do with getting Nanites built and your deposits with some space. Also in the initial part of the game, it is important for the Brain to check often what needs to be built.


#### Autoreasearch
When this setting is set to true, TBot will do the research in the planet set, much like the brain builds things. So you set the levels of technologies you want, a Target planet to do the research, an origin and type of ship to sent the resources to complete the research. It can be the same or a different planet from the `AutoMine` settings.

In the early game, the settings:

      "OptimizeForStart": true,
      "EnsureExpoSlots": true,
      "PrioritizeAstrophysics": true,
      "PrioritizePlasmaTechnology": true,
      "PrioritizeEnergyTechnology": true,
      "PrioritizeIntergalacticResearchNetwork": true,
      "CheckIntervalMin": 10,
      "CheckIntervalMax": 20

  will help you get the Astrophysics going, and in the late game, you can set it to research the only profitable techs, Plasma, Energy ,Intergalactic Research Network, and Astrophysics.


  #### AutoCargo

  
