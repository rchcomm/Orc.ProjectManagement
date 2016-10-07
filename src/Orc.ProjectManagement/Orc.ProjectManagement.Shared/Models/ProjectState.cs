// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ProjectState.cs" company="WildGums">
//   Copyright (c) 2008 - 2016 WildGums. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace Orc.ProjectManagement
{
    using Catel;
    using System;

    public class ProjectState : ICloneable
    {
        public ProjectState()
        {
            
        }

        protected ProjectState(ProjectState projectState)
        {
            Argument.IsNotNull(() => projectState);

            Location = projectState.Location;
            IsLoading = projectState.IsLoading;
            IsSaving = projectState.IsSaving;
            IsRefreshing = projectState.IsRefreshing;
            IsDeactivating = projectState.IsDeactivating;
            IsActivating = projectState.IsActivating;
            IsClosing = projectState.IsClosing;
        }

        public string Location { get; set; }

        public bool IsLoading { get; set; }

        public bool IsClosing { get; set; }

        public bool IsSaving { get; set; }

        public bool IsRefreshing { get; set; }

        public bool IsDeactivating { get; set; }

        public bool IsActivating { get; set; }

        public ProjectState Clone()
        {
            return new ProjectState(this);
        }

        object ICloneable.Clone()
        {
            return Clone();
        }
    }
}