using Game;
using Game.Modding;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Entry point. Registers the bridge system (runs every frame, in menu and in game) and the
    /// agent tool (runs in the tool phase when active), and starts the localhost HTTP API.
    /// </summary>
    public class Mod : IMod
    {
        public const string Version = "0.1.0";

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("ClaudeCityBridge " + Version + " loading");
            updateSystem.UpdateAt<BridgeSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAt<AgentToolSystem>(SystemUpdatePhase.ToolUpdate);
            try { HttpBridge.Start(); }
            catch (System.Exception e) { Log.Error(e, "could not start HTTP bridge"); }
        }

        public void OnDispose()
        {
            HttpBridge.Stop();
            Log.Info("ClaudeCityBridge disposed");
        }
    }
}
