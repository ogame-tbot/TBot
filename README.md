# TBot
OGame Bot

[![GitHub all releases](https://img.shields.io/github/downloads/ogame-tbot/TBot/total)](https://github.com/ogame-tbot/TBot/releases/latest)
[![Discord](https://img.shields.io/discord/801453618770214923)](https://discord.gg/NZSaY4aQ7J)

TBot is a .NET Core 3.1 [OGame](https://lobby.ogame.gameforge.com/) bot based on (a fork of) [ogamed deamon](https://github.com/kokiddp/ogame) by alaingilbert

Feel free to publish issues or pull requests

[![Stories in Ready](https://discordapp.com/api/guilds/801453618770214923/widget.png?style=banner2)](https://discord.gg/NZSaY4aQ7J)

## Disclaimer

Scripting and botting are forbidden by the OGame rules.
I adopt a series of measures to prevent detection and thus ban, but I cannot, and never will, guarantee anything.
Use this bot at your own risk!! Any testing is very much appreciated!

## Features

* Defender: TBot checks periodically for incoming attacks
  * Autofleet: TBot deploys your endangered fleet and resources to your closest celestial
    * Recall: TBot can autorecall the fleet
  * MessageAttacker: TBot sends a message to the attacker(s). The message is picked randomly from the array given in settings.json
  * SpyAttacker: TBot automatically spies attacker with set number of probes
  * Alarm: TBot plays a nasty sound if under attack
  * TelegramMessenger: TBot sends you a notice if under attack (requires additional configuration, see [below](#telegram))
* Expeditions: TBot will handle them for you
  * AutoSendExpeditions: TBot automatically optimizes expeditions for your account, sending them from the origin setted in settings.json
  * AutoHarvest: TBot automatically checks if there are any debris where you send your expeditions, and harvests them
* Brain: TBot has a series of extra functionalities
  * AutoCargo: TBot checks wether your celestials have enough capacity to displace the resources. If not, TBot builds ships for you (preferred type taken from settings.json)
  * AutoRepatriate: TBot periodically repatriates all your resources to a single drop celestial (guess where you can specify which...)
  * AutoMine (tnx to Stewie): Tbot will develop your colonies up to the levels given in settings.json. Transports are not implemented yet, so you should provide resources manually to speed up.
* Local Proxy (tnx to ogamed and CrynogarTM for the hint): Tbot allows you to play in your browser
  * Navigate with your browser to http://127.0.0.1:8080/game/index.php (if you changed the default port modify accordingly)
  * Pay attention: TBot is not aware of what you do in the browser, it will do his job regardless of you playing manually, so keep an eye on the console
  
## Running

* Download latest release
* Insert you credentials in settings.json
  * Under "Universe" type your universe name with leading capital letter, i.e.: Andromeda, Bellatrix etc...
  * Under "Language" type your universe community code. You can find it by logging to your account and analyzing the url, such as s161-us.ogame.gameforge.com => us
* Configure the bot by editing all settings.json fields
  * All config options are sorted by feature, [check which features you](#features) want and configure them before activating
* Make sure you have installed the [.NET Core 3.1 runtime](https://dotnet.microsoft.com/download/dotnet-core/3.1) for your platform
* Run TBot.exe

### Telegram
TBot supports automated Telegram messaging. In order to enable it, you need to follow theese steps:
* Create a new Telegram bot
  * Write "/new_bot" to [@botfather](https://t.me/botfather)
  * Follow the instructions given by BotFather, assigning a name and an username for the bot (theese are not important, set them to whatever you like)
  * BotFather will send you a message containing the API Key you need
  * Insert the newly obtained API Key in settings.json under TelegramMessenger.API
* Get your ChatID
  * Write "/start" to [@getmyid_bot](https://t.me/getmyid_bot)
  * It will answer you a message containing your user ID and chat ID (WARNING: you need the USER ID)
  * Insert the newly obtained ID in settings.json under TelegramMessenger.ChatId
  
## Development Plans
Sleep mode and a better auto fleet save are the next features I plan to write.

Also, a proper documentation about how to deal with settings.json should be written sooner or later.

As for translations, at the moment the bot is not really suitable for "production" and should be used only for testing and hacking purposes. If you are a dev, you can probably cope with my brokenish English. If and when this project becomes something bigger than what it currently is, I may reconsider this.

Feel free to give suggestions posting an Issue or joining the Discord chat.

## Building

I write and build TBot with Visual Studio 2019 Community Edition, thus probably .NET Core 3.1 SDK is enough for command line compilation.
  
## Portability

TBot is currently developed and mantained for Windows 64bit only.

*Short story*

For the time being this will not change.

*Long story*

As you may have noticed, the bot in based upon ogamed, a daemon which exposes an API to interact with an ogame account. Any time TBot needs some data it sends an http request to the daemon, the daemon interacts with ogame and returns the formatted data to TBot. Ogamed is a a part of (the unimaginatively named) ogame, a great GO Lang library. As ogamed did not have all the endpoints I needed, and more in general, I liked a better control over the code, I based TBot on a fork of it. My first choice would have been including the library itself, but I am not aware of any tool for converting a library from GO to .NET, and of course I do not want to rewrite it in C# and loose all the amazing community behind the original ogamed. So I came up with a fairly unconventional idea: I embedded the compiled daemon for one or more OS/arch as a resource, on startup I check for OS and architecture, provide and execute the correct binary. If you are a dev (and if you read so far, you probably are) you are probably either despising or worshiping me for the dirtiest hack ever (i'd lean for the former). In my ToDo/Wish list there are plans for implementing a proper pipeline which builds all the required ogamed binaries, then TBot instead of providing the binary would version check the present binary and, if missing or outdated, download the appropriate binary from the ogamed fork releases. Currently I have no pipeline setup experience. I will catch up someday and fix this, but certainly not today nor until this project grows enough to require it. 

In the future, I plan to release TBot for:
* Windows 64
* Windows 32
* Mac 64
* Mac arm64
* Linux 32
* Linux 64
* Linux arm
* Linux arm64

An Android version could be handy as well, although it may require a serious logic overhaul.

Being extremely lightweight (only ~ 30Mb in RAM), I will prioritize Linux arm and Linux arm64 versions, in order to enable execution on RaspberryPi and similar devices.
As for Mac arm64, I do not own one of theese new gigs. Feel free to contact me if you own such device and are willing to test.
Windows 32 bit, not being really useful, will have the least priority.
