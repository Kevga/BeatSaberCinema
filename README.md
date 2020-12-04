## BeatSaberCinema

A Beat Saber plugin that allows you to sync up videos to play alongside your maps, heavily inspired by MusicVideoPlayer. Made by Dakari#0638

### How to use

The plugin includes more than 30 video configurations for you to try. To do so, download some of the pre-configured levels, a selection of which you can find below:

- [Madeon - The Prince \[Bearly & dgrj4life\]](https://beatsaver.com/beatmap/110ac)
- [2WEI feat. Edda Hayes - Warriors \[halcyon12\]](https://beatsaver.com/beatmap/7e6b)
- [Reol - Utena \[ETAN\]](https://beatsaver.com/beatmap/abc4)
- [JVNA - Catch Me \[nitronik.exe\]](https://beatsaver.com/beatmap/bc4e)
- [Yorushika - Say It \[squeaksies\]](https://beatsaver.com/beatmap/4a21)

After that, select a song in-game and to your left, select "Mods" from the menu. Look for the tab "Cinema", which should look like this:

![Video Menu Screenshot](Images/video-menu.png)

Click download in the center of the menu and wait for the download to complete, then simply play the map. If the download takes too long for you, consider heading back to the main menu and choosing a lower video quality by clicking on "Cinema" in the menu to your left, which takes you to the settings menu.

You can of course add videos to any song you like, even if they are not pre-configured. The menu will in that case show you a button labeled "Search", where you can configure search parameters and choose one of the search results from YouTube. If you do that, you will have to adjust the video offset after the download. To do so, simply click the "Preview" button to play the video, and use the "+" or "-" buttons to adjust the offset until the sound from both ears lines up. Sound from the video will play in your left ear. If the sound from the left ear is behind, click the "+" buttons, otherwise the "-" buttons.

Cinema is also compatible with video configs created with MusicVideoPlayer. You can't however use both plugins at the same time. If MVP is installed as well, Cinema will not be enabled to avoid conflicts.

### Requirements

The following plugins are **required** for Cinema to work:

- BSIPA
- BeatSaberMarkupLanguage
- BS Utils
- SongCore
- CustomJSONData

You can find all of these in ModAssistant.

Additionally, this plugin **conflicts** with:

- MusicVideoPlayer

### JSON Format

The video configs will be stored in the same folder as the map itself, in a file called **cinema-video.json**. Editing the json file by hand allows you to modify some settings that are not available in-game. Cinema includes the ability to change any object in the game scene, to better fit the video screen.

A more detailed explanation for the json format will come soon. For now, here is an example config that uses some of the more advanced settings:

```
{
  "videoID": "_qwnHeMKbVA",
  "title": "Madeon - The Prince (Visual Video)",
  "author": "Chris P",
  "videoFile": "Madeon - The Prince (Visual Video).mp4",
  "duration": 225,
  "offset": -1100,
  "formatVersion": 1,
  "loop": false,
  "screenPosition": {
    "x": 0.0,
    "y": 12.4,
    "z": 100.0
  },
  "disableBigMirrorOverride": true,
  "environment": [
    {
      "name": "RocketArena",
      "active": false
    },
    {
      "name": "RocketGateLight",
      "position": {
        "x": 0.0,
        "y": -3.8,
        "z": 98.0
      },
      "scale": {
        "x": 3.5,
        "y": 1.0,
        "z": 4.8
      }
    }
  ]
}
```

### Special thanks

Special thanks go to:

- **Rolo**:
For creating MVP and helping me improve the looks of the video screen


- **rie-kumar** and **b-rad15**:
For keeping MVP alive across many game updates

- The **youtube-dl** and **ffmpeg** projects:
Used to download the videos. Cinema would not be possible without these.


