﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ProjectWatcherBase.cs" company="Wild Gums">
//   Copyright (c) 2008 - 2015 Wild Gums. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

#if NET40 || SL5
#define USE_TASKEX
#endif

namespace Orc.ProjectManagement
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using Catel;
    using Catel.Data;

    public abstract class ProjectWatcherBase
    {
        #region Fields
        private readonly IProjectManager _projectManager;
        #endregion

        #region Constructors
        protected ProjectWatcherBase(IProjectManager projectManager)
        {
            Argument.IsNotNull(() => projectManager);

            _projectManager = projectManager;

            Init();
        }
        #endregion

        #region Properties
        protected IProjectManager ProjectManager
        {
            get { return _projectManager; }
        }
        #endregion

        private void Init()
        {
            var type = GetType();

            var baseType = type.BaseType;
            if (baseType == null)
            {
                return;
            }

            var methodInfos = from method in type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                              where method.GetBaseDefinition().DeclaringType != method.DeclaringType
                              from subscriber in baseType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                              let methodName = "Subscribe" + method.Name
                              let subscriberName = subscriber.Name
                              let subscriberNameAsync = subscriberName + "Async"
                              where string.Equals(subscriberName, methodName) || string.Equals(subscriberNameAsync, methodName)
                              select subscriber;

            foreach (var methodInfo in methodInfos)
            {
                methodInfo.Invoke(this, new object[0]);
            }
        }

        private void SubscribeOnLoading()
        {
            _projectManager.ProjectLoadingAsync += async (sender, e) => await OnLoadingAsync(e).ConfigureAwait(false);
        }

        private void SubscribeOnLoadingFailed()
        {
            _projectManager.ProjectLoadingFailedAsync += async (sender, e) => await OnLoadingFailedAsync(e.Location, e.Exception, e.ValidationContext).ConfigureAwait(false);
        }

        private void SubscribeOnLoadingCanceled()
        {
            _projectManager.ProjectLoadingCanceledAsync += async (sender, e) => await OnLoadingCanceledAsync(e.Location).ConfigureAwait(false);
        }

        private void SubscribeOnLoaded()
        {
            _projectManager.ProjectLoadedAsync += async (sender, e) => await OnLoadedAsync(e.Project).ConfigureAwait(false);
        }

        private void SubscribeOnSaving()
        {
            _projectManager.ProjectSavingAsync += async (sender, e) => await OnSavingAsync(e).ConfigureAwait(false);
        }

        private void SubscribeOnSavingCanceled()
        {
            _projectManager.ProjectSavingCanceledAsync += async (sender, e) => await OnSavingCanceledAsync(e.Project).ConfigureAwait(false);
        }

        private void SubscribeOnSavingFailed()
        {
            _projectManager.ProjectSavingFailedAsync += async (sender, e) => await OnSavingFailedAsync(e.Project, e.Exception).ConfigureAwait(false);
        }

        private void SubscribeOnSaved()
        {
            _projectManager.ProjectSavedAsync += async (sender, e) => await OnSavedAsync(e.Project).ConfigureAwait(false);
        }

        private void SubscribeOnClosing()
        {
            _projectManager.ProjectClosingAsync += async (sender, e) => await OnClosingAsync(e).ConfigureAwait(false);
        }

        private void SubscribeOnClosed()
        {
            _projectManager.ProjectClosedAsync += async (sender, e) => await OnClosedAsync(e.Project).ConfigureAwait(false);
        }

        private void SubscribeOnClosingCanceled()
        {
            _projectManager.ProjectClosingCanceledAsync += async (sender, e) => await OnClosingCanceledAsync(e.Project).ConfigureAwait(false);
        }

        private void SubscribeOnRefreshRequired()
        {
            _projectManager.ProjectRefreshRequiredAsync += async (sender, e) => await OnProjectRefreshRequiredAsync(e).ConfigureAwait(false);
        }

        private async Task OnProjectRefreshRequiredAsync(ProjectEventArgs e)
        {
            var projects = _projectManager.Projects.Where(project => string.Equals(project.Location, e.Location)).ToList();

            var tasks = projects.Select(OnRefreshRequiredAsync);

#if USE_TASKEX
            await TaskEx.WhenAll(tasks).ConfigureAwait(false);
#else
            await Task.WhenAll(tasks).ConfigureAwait(false);
#endif
        }

        private void SubscribeOnActivated()
        {
            _projectManager.ProjectActivatedAsync += async (sender, e) => await OnActivatedAsync(e.OldProject, e.NewProject).ConfigureAwait(false);
        }

        private void SubscribeOnActivationFailed()
        {
            _projectManager.ProjectActivationFailedAsync += async (sender, e) => await OnActivationFailedAsync(e.Project, e.Exception).ConfigureAwait(false);
        }

        private void SubscribeOnActivationCanceled()
        {
            _projectManager.ProjectActivationCanceledAsync += async (sender, e) => await OnActivationCanceledAsync(e.Project).ConfigureAwait(false);
        }

        private void SubscribeOnActivation()
        {
            _projectManager.ProjectActivationAsync += async (sender, e) => await OnActivationAsync(e).ConfigureAwait(false);
        }

        private void SubscribeOnRefreshed()
        {
            _projectManager.ProjectRefreshedAsync += async (sender, e) => await OnRefreshedAsync(e.Project).ConfigureAwait(false);
        }

        private void SubscribeOnRefreshingFailed()
        {
            _projectManager.ProjectRefreshingFailedAsync += async (sender, e) => await OnRefreshingFailedAsync(e.Project, e.Exception, e.ValidationContext).ConfigureAwait(false);
        }

        private void SubscribeOnRefreshingCanceled()
        {
            _projectManager.ProjectRefreshingCanceledAsync += async (sender, e) => await OnRefreshingCanceledAsync(e.Project).ConfigureAwait(false);
        }

        private void SubscribeOnRefreshing()
        {
            _projectManager.ProjectRefreshingAsync += async (sender, e) => await OnRefreshingAsync(e).ConfigureAwait(false);
        }

        protected virtual async Task OnLoadingAsync(ProjectCancelEventArgs e)
        {
        }

        protected virtual async Task OnLoadingFailedAsync(string location, Exception exception, IValidationContext validationContext)
        {
        }

        protected virtual async Task OnLoadingCanceledAsync(string location)
        {
        }

        protected virtual async Task OnLoadedAsync(IProject project)
        {
        }

        protected virtual async Task OnSavingAsync(ProjectCancelEventArgs e)
        {
        }

        protected virtual async Task OnSavingCanceledAsync(IProject project)
        {
        }

        protected virtual async Task OnSavingFailedAsync(IProject project, Exception exception)
        {
        }

        protected virtual async Task OnSavedAsync(IProject project)
        {
        }

        protected virtual async Task OnClosingAsync(ProjectCancelEventArgs e)
        {
        }

        protected virtual async Task OnClosedAsync(IProject project)
        {
        }

        protected virtual async Task OnClosingCanceledAsync(IProject project)
        {
        }

        protected virtual async Task OnRefreshRequiredAsync(IProject project)
        {
        }

        protected virtual async Task OnActivatedAsync(IProject oldProject, IProject newProject)
        {
        }

        protected virtual async Task OnActivationFailedAsync(IProject project, Exception exception)
        {
        }

        protected virtual async Task OnActivationCanceledAsync(IProject project)
        {
        }

        protected virtual async Task OnActivationAsync(ProjectUpdatingCancelEventArgs e)
        {
        }

        protected virtual async Task OnRefreshedAsync(IProject project)
        {
        }

        protected virtual async Task OnRefreshingFailedAsync(IProject project, Exception exception, IValidationContext validationContext)
        {
        }

        protected virtual async Task OnRefreshingCanceledAsync(IProject project)
        {
        }

        protected virtual async Task OnRefreshingAsync(ProjectCancelEventArgs e)
        {
        }
    }
}