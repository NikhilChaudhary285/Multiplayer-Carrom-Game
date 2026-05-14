🏏 Multiplayer Carrom Game
Unity Multiplayer • Socket.IO • Real-Time Synchronization

A real-time 2-player multiplayer Carrom game built using Unity and a custom Node.js + Socket.IO backend, focused on synchronized gameplay, turn-based logic, and multiplayer architecture.

✨ Highlights
Real-time 2-player multiplayer gameplay
Room creation & join system
Turn-based striker mechanics
Live board synchronization
Server-authoritative turn handling
Score & win condition management
Disconnect handling & room cleanup
⚙️ Engineering Challenge

Building smooth multiplayer synchronization for physics-based Carrom gameplay was challenging because both clients needed consistent board state, turn validation, and synchronized striker movement.

Handling:

Real-time striker shots
Piece pocket detection
Turn switching
Physics synchronization
Duplicate event prevention
Player disconnect recovery

required careful backend validation and client-side synchronization.

✅ Solution

Implemented a custom multiplayer backend using Node.js + Socket.IO with server-authoritative gameplay validation.

Features Implemented
Unique room ID generation
Real-time event broadcasting
Turn validation on server
Sync position relay system
Shot result verification
Score calculation system
Win condition handling
Automatic room cleanup
Multiplayer Flow
Host creates room
Second player joins via room code
Server starts match automatically
Current player fires striker
Backend validates shot & turn
Board sync updates opponent
Server calculates scores & next turn
Winner declared after reaching score limit or board clear
🛠 Tech Stack
Unity
C#
Node.js
Express.js
Socket.IO
Real-Time Networking
Multiplayer Synchronization
📚 Key Learnings

This project helped improve understanding of:

Real-time multiplayer architecture
Socket-based networking
Server-authoritative gameplay systems
Turn-based synchronization
Physics state syncing
Event-driven backend communication
Multiplayer debugging & validation
🔗 Links

🎮 Gameplay Video
[Add Link Here]

💻 GitHub Repository
[Add Link Here]
