![Deceive Logo](http://i.thijsmolendijk.nl/deceive.png)

[![Discord](https://discordapp.com/api/guilds/249481856687407104/widget.png?style=shield)](https://discord.gg/bfxdsRC)

> [!CAUTION]
> The `deceive.gg` website is **UNRELATED** to this project and does not represent this project. Downloading anything from that site is **NOT SAFE**. If you are subscribing to their fake Patreon, you are **BEING SCAMMED**. I do not accept donations and do not run a Patreon project.

# :tophat: Deceive

Deceive allows you to appear offline in League of Legends, VALORANT and Legends of Runeterra without any loss of functionality! Talk to your friends, communicate in champion select and queue up together, all while sneakily appearing offline to all your friends.

Once started, Deceive will be a little icon in your notification tray that allows you to manage your chat presence, whether it be online, offline, or mobile.

# FAQ

### Where can I download Deceive?
Click the [Releases](https://github.com/molenzwiebel/Deceive/releases) tab at the top to download the latest version.

### Can I still invite people? Can they invite me?
Your friends list will work as normal, which means that you can invite everyone. Your friends will not be able to invite you, even if they enter your name manually.

### Can I talk in lobbies/champion/agent select?
Yes, you can talk in lobbies just fine. Only your global "presence" is filtered.

### How do I use Deceive with a specific game?
The first time you launch Deceive, you will be able to choose which game to launch and whether to remember that decision. You can also use the Deceive tray icon to launch a different game.

You can also launch Deceive with `lol`, `lor`, or `valorant` as command-line argument to automatically launch your game of choice.

### Is this approved by Riot?
Riot has confirmed that [you won't get banned](https://i.thijsmolendijk.nl/deceive_ok.png) for using Deceive. It may break at any time though.

### How do I solve the "failing to resolve some required domains" issue?
Deceive works by sitting between the Riot Client and the chat servers. To do that, it needs to intercept traffic, which involves giving the client a different address to connect to. We use `deceive-localhost.molenzwiebel.xyz`, which normally resolves to your local computer. For some network setups/ISPs/school/work networks, this domain does not resolve. If you are on such a network, you'll need to either change your DNS to something like [Cloudflare's 1.1.1.1](https://developers.cloudflare.com/1.1.1.1/setup/windows/) or [Google's 8.8.8.8](https://developers.google.com/speed/public-dns/docs/using), or [manually add](https://kb.parallels.com/en/129398) the entry `127.0.0.1 deceive-localhost.molenzwiebel.xyz` to your `hosts` file. Note that for both of these options you will need Administrator access on your PC. If you are having trouble with this step, Google or LLMs like ChatGPT can usually walk you through the required steps.

### I'm more of a visual learner. Do you have a video?
Sure thing! Just click the preview below:  
[![Youtube Preview](http://img.youtube.com/vi/bfsbtd39GqE/maxresdefault.jpg)](https://youtu.be/bfsbtd39GqE)
