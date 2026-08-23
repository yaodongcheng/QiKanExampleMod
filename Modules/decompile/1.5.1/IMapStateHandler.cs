using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Incidents;
using TaleWorlds.Library;

namespace TaleWorlds.CampaignSystem.GameState;

public interface IMapStateHandler
{
	void OnRefreshState();

	void OnMainPartyEncounter();

	void OnIncidentStarted(Incident incident);

	void BeforeTick(float dt);

	void Tick(float dt);

	void AfterTick(float dt);

	void AfterWaitTick(float dt);

	void OnIdleTick(float dt);

	void OnSignalPeriodicEvents();

	void OnExit();

	void ResetCamera(bool resetDistance, bool teleportToMainParty);

	void TeleportCameraToMainParty();

	void FastMoveCameraToMainParty();

	bool IsCameraLockedToPlayerParty();

	void StartCameraAnimation(CampaignVec2 targetPosition, float animationStopDuration);

	void OnHourlyTick();

	void OnMenuModeTick(float dt);

	void OnEnteringMenuMode(MenuContext menuContext);

	void OnExitingMenuMode();

	void OnBattleSimulationStarted(BattleSimulation battleSimulation);

	void OnBattleSimulationEnded();

	void OnGameplayCheatsEnabled();

	void OnMapConversationStarts(ConversationCharacterData playerCharacterData, ConversationCharacterData conversationPartnerData);

	void OnMapConversationOver();

	void OnPlayerSiegeActivated();

	void OnPlayerSiegeDeactivated();

	void OnSiegeEngineClick(MatrixFrame siegeEngineFrame);

	void OnGameLoadFinished();
}
You are not using the latest version of the tool, please update.
Latest version is '11.0.0.9375' (yours is '8.2.0.7535-95108c96')
