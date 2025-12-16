const { Client, GatewayIntentBits, EmbedBuilder, ActionRowBuilder, ButtonBuilder, ButtonStyle, REST, Routes, ActivityType } = require('discord.js');
const express = require('express');
const bodyParser = require('body-parser');
const fs = require('fs').promises;
const path = require('path');

const CONFIG = {
  DISCORD_TOKEN: 'put your discord token here',
  SERVER_ID: 'put your server id here',
  CHANNEL_ID: 'put your verify chanel id here',
  VERIFIED_ROLE_ID: 'put ur verify role here',
  PORT: 3000,
  DATA_FILE: './linked_accounts.json',
  MESSAGE_ID_FILE: './verification_message.json'
};

const linkingCodes = new Map(); 
const linkedAccounts = new Map(); 
let verificationMessageId = null;

const client = new Client({
  intents: [
    GatewayIntentBits.Guilds,
    GatewayIntentBits.GuildMessages,
    GatewayIntentBits.MessageContent,
    GatewayIntentBits.GuildMembers
  ]
});

const app = express();
app.use(bodyParser.json());

const statusMessages = ['67', '41', 'Made by Gaty'];
let currentStatusIndex = 0;

function rotateStatus() {
  client.user.setPresence({
    activities: [{
      name: `Verifying players | ${statusMessages[currentStatusIndex]}`,
      type: ActivityType.Playing
    }],
    status: 'online'
  });
  
  currentStatusIndex = (currentStatusIndex + 1) % statusMessages.length;
}

async function saveData() {
  try {
    const data = {
      linkedAccounts: Array.from(linkedAccounts.entries())
    };
    await fs.writeFile(CONFIG.DATA_FILE, JSON.stringify(data, null, 2));
  } catch (error) {
    console.error('Error saving data:', error);
  }
}

async function saveMessageId() {
  try {
    await fs.writeFile(CONFIG.MESSAGE_ID_FILE, JSON.stringify({ messageId: verificationMessageId }));
  } catch (error) {
    console.error('Error saving message ID:', error);
  }
}

async function loadMessageId() {
  try {
    const fileExists = await fs.access(CONFIG.MESSAGE_ID_FILE).then(() => true).catch(() => false);
    if (fileExists) {
      const data = await fs.readFile(CONFIG.MESSAGE_ID_FILE, 'utf8');
      const parsed = JSON.parse(data);
      verificationMessageId = parsed.messageId;
    }
  } catch (error) {
    console.error('Error loading message ID:', error);
  }
}

async function loadData() {
  try {
    const fileExists = await fs.access(CONFIG.DATA_FILE).then(() => true).catch(() => false);
    if (!fileExists) {
      return;
    }

    const data = await fs.readFile(CONFIG.DATA_FILE, 'utf8');
    const parsed = JSON.parse(data);
    
    if (parsed.linkedAccounts) {
      for (const [discordId, accountData] of parsed.linkedAccounts) {
        linkedAccounts.set(discordId, accountData);
      }
    }
  } catch (error) {
    console.error('Error loading data:', error);
  }
}

async function assignVerifiedRole(discordId) {
  try {
    const guild = await client.guilds.fetch(CONFIG.SERVER_ID);
    const member = await guild.members.fetch(discordId);
    const role = await guild.roles.fetch(CONFIG.VERIFIED_ROLE_ID);
    
    if (!member.roles.cache.has(CONFIG.VERIFIED_ROLE_ID)) {
      await member.roles.add(role);
      return true;
    }
    return false;
  } catch (error) {
    console.error('Error assigning verified role:', error);
    return false;
  }
}

function generateCode() {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  let code = '';
  for (let i = 0; i < 10; i++) {
    code += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return code;
}

async function registerCommands() {
  const commands = [];

  const rest = new REST({ version: '10' }).setToken(CONFIG.DISCORD_TOKEN);

  try {
    await rest.put(
      Routes.applicationGuildCommands(client.user.id, CONFIG.SERVER_ID),
      { body: commands }
    );
  } catch (error) {
    console.error('Error registering commands:', error);
  }
}

client.once('ready', async () => {
  console.log(`Bot logged in as ${client.user.tag}`);
  
  await loadData();
  await loadMessageId();
  
  for (const [discordId, account] of linkedAccounts) {
    await assignVerifiedRole(discordId);
  }
  
  await registerCommands();
  
  rotateStatus();
  setInterval(rotateStatus, 10000);
  
  await sendVerificationMessage();
});

async function sendVerificationMessage() {
  try {
    const channel = await client.channels.fetch(CONFIG.CHANNEL_ID);
    
    const embed = new EmbedBuilder()
      .setTitle('Verification')
      .setDescription('Click the verify button to verify your account for a lot of features.')
      .setColor(0x637b64);
    
    const row = new ActionRowBuilder()
      .addComponents(
        new ButtonBuilder()
          .setCustomId('verify_account')
          .setLabel('Verify')
          .setStyle(ButtonStyle.Success)
      );
    
    if (verificationMessageId) {
      try {
        const message = await channel.messages.fetch(verificationMessageId);
        await message.edit({ embeds: [embed], components: [row] });
      } catch (error) {
        const message = await channel.send({ embeds: [embed], components: [row] });
        verificationMessageId = message.id;
        await saveMessageId();
      }
    } else {
      const message = await channel.send({ embeds: [embed], components: [row] });
      verificationMessageId = message.id;
      await saveMessageId();
    }
  } catch (error) {
    console.error('Error sending verification message:', error);
  }
}

client.on('interactionCreate', async (interaction) => {
  if (interaction.isButton()) {
    if (interaction.customId === 'verify_account') {
      const userId = interaction.user.id;
      
      if (linkedAccounts.has(userId)) {
        const account = linkedAccounts.get(userId);
        await interaction.reply({
          content: `Your account is already linked to Steam ID: ${account.steamId}`,
          ephemeral: true
        });
        return;
      }
      
      const code = generateCode();
      linkingCodes.set(code, {
        discordId: userId,
        steamId: null,
        timestamp: Date.now()
      });

      setTimeout(() => {
        if (linkingCodes.has(code)) {
          linkingCodes.delete(code);
        }
      }, 10 * 60 * 1000);
      
      await interaction.reply({
        content: `Do \`/link ${code}\` in-game to link your account.\nThis code expires in 10 minutes.`,
        ephemeral: true
      });
    }
  }
});

app.post('/api/link', async (req, res) => {
  const { code, steamId, steamName } = req.body;
  
  if (!code || !steamId || !steamName) {
    return res.status(400).json({ success: false, message: 'Missing required fields' });
  }
  
  if (!linkingCodes.has(code)) {
    return res.status(404).json({ success: false, message: 'Invalid or expired code' });
  }
  
  const linkData = linkingCodes.get(code);

  for (const [discordId, account] of linkedAccounts) {
    if (account.steamId === steamId) {
      linkingCodes.delete(code);
      return res.status(400).json({ 
        success: false, 
        message: 'This Steam account is already linked to another Discord account' 
      });
    }
  }
  
  linkedAccounts.set(linkData.discordId, { steamId, steamName });
  linkingCodes.delete(code);
  
  await saveData();
  await assignVerifiedRole(linkData.discordId);
  
  try {
    const user = await client.users.fetch(linkData.discordId);
    await user.send(`Your account has been successfully linked to Steam account: ${steamName} (${steamId})\nYou've been granted the verified role!`);
  } catch (error) {
    console.error('Could not DM user:', error);
  }
  
  res.json({ success: true, message: 'Account linked successfully' });
});

app.get('/api/check/:steamId', (req, res) => {
  const { steamId } = req.params;
  
  for (const [discordId, account] of linkedAccounts) {
    if (account.steamId === steamId) {
      return res.json({ linked: true, discordId, steamName: account.steamName });
    }
  }
  
  res.json({ linked: false });
});

app.get('/api/account/:discordId', (req, res) => {
  const { discordId } = req.params;
  
  if (linkedAccounts.has(discordId)) {
    res.json({ linked: true, account: linkedAccounts.get(discordId) });
  } else {
    res.json({ linked: false });
  }
});

app.listen(CONFIG.PORT, () => {
  console.log(`API server running on port ${CONFIG.PORT}`);
});

client.login(CONFIG.DISCORD_TOKEN);
