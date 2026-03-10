get; set; }

		// Token: 0x17000412 RID: 1042
		// (get) Token: 0x0600165D RID: 5725 RVA: 0x00151BE8 File Offset: 0x0014FDE8
		// (set) Token: 0x0600165E RID: 5726 RVA: 0x00151BFC File Offset: 0x0014FDFC
		public string Description { get; set; }

		// Token: 0x0600165F RID: 5727 RVA: 0x00151C10 File Offset: 0x0014FE10
		public TaskStep()
		{
			this.Status = TaskStepStatus.Pending;
		}

		// Token: 0x06001660 RID: 5728 RVA: 0x00151C30 File Offset: 0x0014FE30
		public TaskStep(TaskStepType A_1, string A_2 = null)
		{
			this.StepType = A_1;
			this.Status = TaskStepStatus.Pending;
			this.Description = A_2;
		}

		// Token: 0x06001661 RID: 5729 RVA: 0x00151C60 File Offset: 0x0014FE60
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static TaskStep CreateGoToSettlement(Settlement settlement, string description = null)
		{
			return new TaskStep(TaskStepType.GoToSettlement, description ?? \u200B\u200E\u202C\u206F\u202C\u202E\u206E\u200D\u202B\u202A\u202E\u202E\u200E\u202D\u202E\u200F\u202D\u202B\u202B\u202C\u202A\u206A\u200B\u202D\u202E\u202B\u200F\u202B\u206F\u202C\u200B\u206A\u202C\u202E\u200D\u202B\u200F\u200D\u202A\u200C\u202E.\u206B\u206E\u200F\u200B\u202C\u202E\u202A\u202B\u206F\u206F\u202D\u200F\u202B\u202B\u202E\u200B\u206D\u200C\u206B\u206F\u200F\u200D\u206D\u202B\u200B\u206F\u202C\u200B\u200D\u206C\u200F\u202C\u200E\u200D\u202A\u206F\u206D\u200E\u200F\u206A\u202E(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(279859592), (settlement != null) ? \u200F\u202A\u200F\u200D\u202E\u206B\u202D\u202D\u206C\u200C\u202C\u206E\u206E\u206C\u200B\u206F\u206D\u200C\u200B\u206E\u202E\u206D\u206D\u206B\u200D\u200B\u206E\u206C\u206D\u202C\u200E\u206A\u202D\u206E\u202A\u202D\u202D\u202C\u200E\u202D\u202E.\u000AY\u00ADM((settlement) : null))
			{
				TargetSettlementId = ((settlement != null) ? \u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.\u000Bûéï\u00A9(settlement) : null)
			};
		}

		// Token: 0x06001662 RID: 5730 RVA: 0x00151CB8 File Offset: 0x0014FEB8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static TaskStep CreateWaitInSettlement(Settlement settlement, float waitDays, string description = null)
		{
			return new TaskStep(TaskStepType.WaitInSettlement, description ?? \u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.\u206E\u200E\u200C\u202C\u206A\u206D\u200F\u202A\u202E\u206C\u206B\u206A\u206A\u206E\u202B\u206D\u202E\u202B\u200E\u200B\u200F\u206B\u200F\u200C\u202E\u206F\u200F\u200F\u200F\u202C\u200C\u206A\u206F\u206E\u200E\u200C\u206A\u206B\u200B\u206C\u202E(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1050920133), (settlement != null) ? \u200F\u202A\u200F\u200D\u202E\u206B\u202D\u202D\u206C\u200C\u202C\u206E\u206E\u206C\u200B\u206F\u206D\u200C\u200B\u206E\u202E\u206D\u206D\u206B\u200D\u200B\u206E\u206C\u206D\u202C\u200E\u206A\u202D\u206E\u202A\u202D\u202D\u202C\u200E\u202D\u202E.\u000AY\u00ADM((settlement) : null, waitDays))
			{
				TargetSettlementId = ((settlement != null) ? \u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.\u000Bûéï\u00A9(settlement) : null),
				WaitDays = waitDays
			};
		}

		// Token: 0x06001663 RID: 5731 RVA: 0x00151D20 File Offset: 0x0014FF20
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static TaskStep CreateReturnToPlayer(string description = null)
		{
			return 