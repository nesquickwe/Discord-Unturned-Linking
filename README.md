# Discord-Unturned Link Bot

Links Discord accounts with Unturned/Steam for verification and rewards.

## What it does

- Players click verify in Discord, get a code
- Use `/link [code]` in-game to connect accounts
- Gets verified role automatically
- Bot status rotates between custom messages
- Saves everything so restarts don't break links

## Setup

### Get a Discord bot

1. Make a bot at https://discord.dev
2. Turn on Server Members Intent and Message Content Intent under Bot settings
3. Copy the token
4. Invite with these permissions: Manage Roles, Send Messages, Use Slash Commands

### Get your server info

Enable Developer Mode in Discord settings, then right-click and copy IDs for:
- Your server
- Verification channel
- Verified role

### Install

```bash
npm install discord.js express body-parser
```

Edit the CONFIG at the top of the file:
```javascript
const CONFIG = {
  DISCORD_TOKEN: 'paste_token_here',
  SERVER_ID: 'server_id_here',
  CHANNEL_ID: 'channel_id_here',
  VERIFIED_ROLE_ID: 'role_id_here',
  PORT: 3000,
  DATA_FILE: './linked_accounts.json',
  MESSAGE_ID_FILE: './verification_message.json'
};
```

Run it:
```bash
node bot.js
```

### Connect to your Unturned server

The API runs on port 3000. Your Unturned plugin needs to:

**For linking:**
POST to `/api/link` with:
- code (from Discord)
- steamId
- steamName

**Check links:**
GET `/api/check/:steamId` or `/api/account/:discordId`

## How to use

1. Player clicks Verify in Discord channel
2. Gets a code (expires in 10 minutes)
3. Types `/link [code]` in game
4. Bot DMs them confirmation and gives role

## Customize

Change status messages:
```javascript
const statusMessages = ['67', '41', 'Made by Gaty'];
```

Change rotation speed (milliseconds):
```javascript
setInterval(rotateStatus, 10000);
```

## Common issues

**Commands don't show up** - Wait a few minutes or restart Discord

**Button doesn't work** - Check bot permissions and channel ID

**Links don't save** - Make sure bot can write files in its folder

**Role not given** - Bot's role must be above the verified role in server settings

## Files created

- `linked_accounts.json` - Stores all linked accounts
- `verification_message.json` - Saves message ID for editing on restart

Don't share your bot token. Back up the JSON files occasionally.
