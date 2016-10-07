// --------------------------------------------------------------------------------------------------------------------
// <copyright file="IProjectStateService.cs" company="WildGums">
//   Copyright (c) 2008 - 2016 WildGums. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace Orc.ProjectManagement
{
    using System;

    public interface IProjectStateService
    {
        #region Properties
        bool IsRefreshingActiveProject { get; }
        #endregion

        #region Events
        event EventHandler<EventArgs> IsRefreshingActiveProjectUpdated;
        event EventHandler<ProjectStateEventArgs> ProjectStateUpdated;
        #endregion

        #region Methods
        [ObsoleteEx(ReplacementTypeOrMember = "GetState", TreatAsErrorFromVersion = "1.0", RemoveInVersion = "2.0")]
        ProjectState GetProjectState(IProject project);
        ProjectState GetState(string progectLocation);
        void UpdateState(string location, Action<ProjectState> updateAction);
        #endregion
    }
}