# K-OCR Architecture Guidelines
 
 ## Real-Time Communication: Signal-Based Over Polling
 
 When recommending or implementing features that require status updates, user feedback, or real-time notifications:
 
 1. **Prioritize signal-based approaches:**
    - SignalR (WebSocket-based duplex communication)
    - Server-Sent Events (SSE) for one-way server-to-client
    - WebSockets (raw)
    - Event-driven architectures
 
 2. **Avoid polling except when:**
    - Signal-based approaches are not feasible (legacy browsers, etc.)
    - The operation is infrequent (e.g., guest account expiry check every 30 seconds)
    - Document the polling as a temporary measure
 
 3. **Current State:**
    - Backend: `System.Reactive` installed; `BatchChangeNotifier` (Rx.NET Subject) ready for broadcast
    - Frontend: RxJS not bundled; vanilla JS with `setInterval` polling
    - Goal: Connect backend Rx streams to SignalR hub → frontend RxJS subscriptions
 
 ## Examples
 
 ### ❌ NOT PREFERRED (Current OCR polling)
 ```javascript
 setInterval(() => fetch('/home/getocrstatus'), 3000);

✅ PREFERRED (SignalR alternative)

 // Backend: Wire BatchChangeNotifier to SignalR
 public class BatchUpdateHub : Hub {
     public async Task SubscribeToOcrUpdates(int batchId) { ... }
 }

 // Frontend: Subscribe to real-time updates
 connection.on("OcrProgress", (processed, total) => { ... });
 
 When Making Recommendations

 - Suggest SignalR/SSE first
 - Only recommend polling if no signal-based alternative exists
 - Flag polling implementations for future refactoring