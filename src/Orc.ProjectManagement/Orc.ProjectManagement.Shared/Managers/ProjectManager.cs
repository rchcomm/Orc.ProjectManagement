﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ProjectManager.cs" company="Wild Gums">
//   Copyright (c) 2008 - 2015 Wild Gums. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

#if NET40 || SL5
#define USE_TASKEX
#endif

namespace Orc.ProjectManagement
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Catel;
    using Catel.Collections;
    using Catel.Data;
    using Catel.IoC;
    using Catel.Logging;
    using Catel.Reflection;

    internal class ProjectManager : IProjectManager, INeedCustomInitialization
    {
        #region Fields
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();
        private readonly IProjectInitializer _projectInitializer;
        private readonly IProjectManagementInitializationService _projectManagementInitializationService;
        private readonly IDictionary<string, IProjectRefresher> _projectRefreshers;
        private readonly IProjectRefresherSelector _projectRefresherSelector;
        private readonly ListDictionary<string, IProject> _projects;
        private readonly IProjectSerializerSelector _projectSerializerSelector;
        private readonly IProjectValidator _projectValidator;
        private bool _isLoading;
        private int _savingCounter;
        #endregion

        #region Constructors
        public ProjectManager(IProjectValidator projectValidator, IProjectRefresherSelector projectRefresherSelector, IProjectSerializerSelector projectSerializerSelector,
            IProjectInitializer projectInitializer, IProjectManagementConfigurationService projectManagementConfigurationService,
            IProjectManagementInitializationService projectManagementInitializationService)
        {
            Argument.IsNotNull(() => projectValidator);
            Argument.IsNotNull(() => projectRefresherSelector);
            Argument.IsNotNull(() => projectSerializerSelector);
            Argument.IsNotNull(() => projectInitializer);
            Argument.IsNotNull(() => projectManagementConfigurationService);
            Argument.IsNotNull(() => projectManagementInitializationService);

            _projectValidator = projectValidator;
            _projectRefresherSelector = projectRefresherSelector;
            _projectSerializerSelector = projectSerializerSelector;
            _projectInitializer = projectInitializer;
            _projectManagementInitializationService = projectManagementInitializationService;

            _projects = new ListDictionary<string, IProject>();
            _projectRefreshers = new ConcurrentDictionary<string, IProjectRefresher>();

            ProjectManagementType = projectManagementConfigurationService.GetProjectManagementType();
        }
        #endregion

        #region Properties
        public ProjectManagementType ProjectManagementType { get; private set; }

        public virtual IEnumerable<IProject> Projects
        {
            get { return _projects.Select(x => x.Value); }
        }

        public virtual IProject ActiveProject { get; set; }
        #endregion

        #region Methods
        void INeedCustomInitialization.Initialize()
        {
            _projectManagementInitializationService.Initialize(this);
        }
        #endregion

        #region Events
        public event EventHandler<ProjectEventArgs> ProjectRefreshRequiredAsync;

        public event AsyncEventHandler<ProjectCancelEventArgs> ProjectLoadingAsync;
        public event AsyncEventHandler<ProjectErrorEventArgs> ProjectLoadingFailedAsync;
        public event AsyncEventHandler<ProjectEventArgs> ProjectLoadingCanceledAsync;
        public event AsyncEventHandler<ProjectEventArgs> ProjectLoadedAsync;

        public event AsyncEventHandler<ProjectCancelEventArgs> ProjectSavingAsync;
        public event AsyncEventHandler<ProjectErrorEventArgs> ProjectSavingFailedAsync;
        public event AsyncEventHandler<ProjectEventArgs> ProjectSavingCanceledAsync;
        public event AsyncEventHandler<ProjectEventArgs> ProjectSavedAsync;

        public event AsyncEventHandler<ProjectCancelEventArgs> ProjectRefreshingAsync;
        public event AsyncEventHandler<ProjectEventArgs> ProjectRefreshedAsync;
        public event AsyncEventHandler<ProjectEventArgs> ProjectRefreshingCanceledAsync;
        public event AsyncEventHandler<ProjectErrorEventArgs> ProjectRefreshingFailedAsync;

        public event AsyncEventHandler<ProjectCancelEventArgs> ProjectClosingAsync;
        public event AsyncEventHandler<ProjectEventArgs> ProjectClosingCanceledAsync;
        public event AsyncEventHandler<ProjectEventArgs> ProjectClosedAsync;

        public event AsyncEventHandler<ProjectUpdatingCancelEventArgs> ProjectActivationAsync;
        public event AsyncEventHandler<ProjectUpdatedEventArgs> ProjectActivatedAsync;
        public event AsyncEventHandler<ProjectEventArgs> ProjectActivationCanceledAsync;
        public event AsyncEventHandler<ProjectErrorEventArgs> ProjectActivationFailedAsync;
        #endregion

        #region IProjectManager Members
        public async Task InitializeAsync()
        {
            var tasks = _projectInitializer.GetInitialLocations().Where(x => !string.IsNullOrWhiteSpace(x)).Select(location =>
            {
                Log.Debug("Loading initial project from location '{0}'", location);
                return LoadAsync(location);
            });
#if USE_TASKEX
            await TaskEx.WhenAll(tasks).ConfigureAwait(false);
#else
            await Task.WhenAll(tasks).ConfigureAwait(false);
#endif
        }

        public async Task<bool> RefreshAsync()
        {
            var project = ActiveProject;
            if (project == null)
            {
                return false;
            }

            return await RefreshAsync(project).ConfigureAwait(false);
        }

        public virtual async Task<bool> RefreshAsync(IProject project)
        {
            Argument.IsNotNull(() => project);

            var projectLocation = project.Location;

            var activeProjectLocation = this.GetActiveProjectLocation();

            Log.Debug("Refreshing project from '{0}'", projectLocation);

            var cancelEventArgs = new ProjectCancelEventArgs(projectLocation);

            await ProjectRefreshingAsync.SafeInvokeAsync(this, cancelEventArgs).ConfigureAwait(false);

            Exception error = null;
            IValidationContext validationContext = null;

            try
            {
                if (cancelEventArgs.Cancel)
                {
                    await ProjectRefreshingCanceledAsync.SafeInvokeAsync(this, new ProjectErrorEventArgs(project)).ConfigureAwait(false);
                    return false;
                }

                var isRefreshingActiveProject = string.Equals(activeProjectLocation, projectLocation);

                UnregisterProject(project);

                if (isRefreshingActiveProject)
                {
                    await SetActiveProjectAsync(null).ConfigureAwait(false);
                }

                var loadedProject = await QuietlyLoadProject(projectLocation).ConfigureAwait(false);

                validationContext = _projectValidator.ValidateProject(loadedProject);
                if (validationContext.HasErrors)
                {
                    Log.ErrorAndThrowException<InvalidOperationException>(string.Format("Project data was loaded from '{0}', but the validator returned errors", projectLocation));
                }

                RegisterProject(loadedProject);

                await ProjectRefreshedAsync.SafeInvokeAsync(this, new ProjectEventArgs(loadedProject)).ConfigureAwait(false);

                if (isRefreshingActiveProject)
                {
                    await SetActiveProjectAsync(loadedProject).ConfigureAwait(false);
                }

                Log.Info("Refreshed project from '{0}'", projectLocation);
            }
            catch (Exception exception)
            {
                error = exception;
            }

            if (error == null)
            {
                return true;
            }

            var eventArgs = new ProjectErrorEventArgs(project,
                new ProjectException(project, string.Format("Failed to load project from location '{0}' while refreshing.", projectLocation), error),
                validationContext);

            await ProjectRefreshingFailedAsync.SafeInvokeAsync(this, eventArgs).ConfigureAwait(false);

            return false;
        }

        public virtual async Task<bool> LoadAsync(string location)
        {
            Argument.IsNotNullOrWhitespace("location", location);

            var project = await LoadProjectAsync(location).ConfigureAwait(false);

            await SetActiveProjectAsync(project).ConfigureAwait(false);

            return project != null;
        }

        public virtual async Task<bool> LoadInactiveAsync(string location)
        {
            Argument.IsNotNullOrWhitespace("location", location);

            var project = await LoadProjectAsync(location).ConfigureAwait(false);

            return project != null;
        }

        private async Task<IProject> LoadProjectAsync(string location)
        {
            Argument.IsNotNullOrWhitespace("location", location);

            var project = Projects.FirstOrDefault(x => string.Equals(location, x.Location, StringComparison.OrdinalIgnoreCase));
            if (project != null)
            {
                return project;
            }

            using (new DisposableToken(null, token => _isLoading = true, token => _isLoading = false))
            {
                Log.Debug("Loading project from '{0}'", location);

                var cancelEventArgs = new ProjectCancelEventArgs(location);

                await ProjectLoadingAsync.SafeInvokeAsync(this, cancelEventArgs).ConfigureAwait(false);

                if (cancelEventArgs.Cancel)
                {
                    Log.Debug("Canceled loading of project from '{0}'", location);
                    await ProjectLoadingCanceledAsync.SafeInvokeAsync(this, new ProjectEventArgs(location)).ConfigureAwait(false);

                    return null;
                }

                Exception error = null;

                IValidationContext validationContext = null;

                try
                {
                    if (_projects.Count > 0 && ProjectManagementType == ProjectManagementType.SingleDocument)
                    {
                        Log.ErrorAndThrowException<SdiProjectManagementException>("Cannot load project '{0}', currently in SDI mode", location);
                    }

                    project = await QuietlyLoadProject(location).ConfigureAwait(false);

                    validationContext = _projectValidator.ValidateProject(project);
                    if (validationContext.HasErrors)
                    {
                        Log.ErrorAndThrowException<InvalidOperationException>(string.Format("Project data was loaded from '{0}', but the validator returned errors", location));
                    }

                    RegisterProject(project);
                }
                catch (Exception ex)
                {
                    error = ex;
                    Log.Error(ex, "Failed to load project from '{0}'", location);
                }

                if (error != null)
                {
                    await ProjectLoadingFailedAsync.SafeInvokeAsync(this, new ProjectErrorEventArgs(location, error, validationContext)).ConfigureAwait(false);

                    return null;
                }

                await ProjectLoadedAsync.SafeInvokeAsync(this, new ProjectEventArgs(project)).ConfigureAwait(false);

                Log.Info("Loaded project from '{0}'", location);
            }

            return project;
        }

        private void RegisterProject(IProject project)
        {
            var projectLocation = project.Location;
            _projects[projectLocation] = project;

            InitializeProjectRefresher(projectLocation);
        }

        private async Task<IProject> QuietlyLoadProject(string location)
        {
            Log.Debug("Validating to see if we can load the project from '{0}'", location);

            if (!_projectValidator.CanStartLoadingProject(location))
            {
                throw new ProjectException(location, String.Format("Cannot load project from '{0}'", location));
            }

            var projectReader = _projectSerializerSelector.GetReader(location);
            if (projectReader == null)
            {
                Log.ErrorAndThrowException<InvalidOperationException>(string.Format("No project reader is found for location '{0}'", location));
            }

            Log.Debug("Using project reader '{0}'", projectReader.GetType().Name);

            var project = await projectReader.ReadAsync(location);
            if (project == null)
            {
                Log.ErrorAndThrowException<InvalidOperationException>(string.Format("Project could not be loaded from '{0}'", location));
            }

            return project;
        }

        public async Task<bool> SaveAsync(string location = null)
        {
            var project = ActiveProject;
            if (project == null)
            {
                Log.Error("Cannot save empty project");
                return false;
            }

            if (string.IsNullOrWhiteSpace(location))
            {
                location = project.Location;
            }

            return await SaveAsync(project, location).ConfigureAwait(false);
        }

        public async Task<bool> SaveAsync(IProject project, string location = null)
        {
            Argument.IsNotNull(() => project);

            if (string.IsNullOrWhiteSpace(location))
            {
                location = project.Location;
            }

            using (new DisposableToken(null, token => _savingCounter++, token => _savingCounter--))
            {
                Log.Debug("Saving project '{0}' to '{1}'", project, location);

                var cancelEventArgs = new ProjectCancelEventArgs(project);
                await ProjectSavingAsync.SafeInvokeAsync(this, cancelEventArgs).ConfigureAwait(false);

                if (cancelEventArgs.Cancel)
                {
                    Log.Debug("Canceled saving of project to '{0}'", location);
                    await ProjectSavingCanceledAsync.SafeInvokeAsync(this, new ProjectEventArgs(project)).ConfigureAwait(false);
                    return false;
                }

                var projectWriter = _projectSerializerSelector.GetWriter(location);
                if (projectWriter == null)
                {
                    Log.ErrorAndThrowException<NotSupportedException>(string.Format("No project writer is found for location '{0}'", location));
                }

                Log.Debug("Using project writer '{0}'", projectWriter.GetType().Name);

                Exception error = null;
                bool success = true;
                try
                {
                    success = await projectWriter.WriteAsync(project, location).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                if (error != null)
                {
                    Log.Error(error, "Failed to save project '{0}' to '{1}'", project, location);
                    await ProjectSavingFailedAsync.SafeInvokeAsync(this, new ProjectErrorEventArgs(project, error)).ConfigureAwait(false);

                    return false;
                }

                if (!success)
                {
                    Log.Error("Failed to save project '{0}' to '{1}'", project, location);
                    return false;
                }

                await ProjectSavedAsync.SafeInvokeAsync(this, new ProjectEventArgs(project)).ConfigureAwait(false);

                var projectString = project.ToString();
                Log.Info("Saved project '{0}' to '{1}'", projectString, location);
            }

            return true;
        }

        public async Task<bool> CloseAsync()
        {
            var project = ActiveProject;
            if (project == null)
            {
                return false;
            }

            return await CloseAsync(project).ConfigureAwait(false);
        }

        public virtual async Task<bool> CloseAsync(IProject project)
        {
            Argument.IsNotNull(() => project);

            Log.Debug("Closing project '{0}'", project);

            var cancelEventArgs = new ProjectCancelEventArgs(project);
            await ProjectClosingAsync.SafeInvokeAsync(this, cancelEventArgs).ConfigureAwait(false);

            if (cancelEventArgs.Cancel)
            {
                Log.Debug("Canceled closing project '{0}'", project);
                await ProjectClosingCanceledAsync.SafeInvokeAsync(this, new ProjectEventArgs(project)).ConfigureAwait(false);
                return false;
            }

            await SetActiveProjectAsync(null).ConfigureAwait(false);

            UnregisterProject(project);

            await ProjectClosedAsync.SafeInvokeAsync(this, new ProjectEventArgs(project)).ConfigureAwait(false);

            Log.Info("Closed project '{0}'", project);

            return true;
        }

        private void UnregisterProject(IProject project)
        {
            var location = project.Location;
            if (_projects.ContainsKey(location))
            {
                _projects.Remove(location);
            }

            ReleaseProjectRefresher(project);
        }

        public virtual async Task<bool> SetActiveProjectAsync(IProject project)
        {
            var activeProject = ActiveProject;

            var activeProjectLocation = activeProject == null ? null : activeProject.Location;
            var newProjectLocation = project == null ? null : project.Location;

            if (string.Equals(activeProjectLocation, newProjectLocation))
            {
                return false;
            }

            Log.Info(project != null
                ? string.Format("Activating project '{0}'", project.Location)
                : "Deactivating currently active project");

            var eventArgs = new ProjectUpdatingCancelEventArgs(activeProject, project);

            await ProjectActivationAsync.SafeInvokeAsync(this, eventArgs).ConfigureAwait(false);

            if (eventArgs.Cancel)
            {
                Log.Info(project != null
                    ? string.Format("Activating project '{0}' was canceled", project.Location)
                    : "Deactivating currently active project");

                await ProjectActivationCanceledAsync.SafeInvokeAsync(this, new ProjectEventArgs(project)).ConfigureAwait(false);
                return false;
            }

            Exception exception = null;

            try
            {
                ActiveProject = project;
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            if (exception != null)
            {
                Log.Error(exception, project != null
                    ? string.Format("Failed to activate project '{0}'", project.Location)
                    : "Failed to deactivate currently active project");

                await ProjectActivationFailedAsync.SafeInvokeAsync(this, new ProjectErrorEventArgs(project, exception)).ConfigureAwait(false);
                return false;
            }

            await ProjectActivatedAsync.SafeInvokeAsync(this, new ProjectUpdatedEventArgs(activeProject, project)).ConfigureAwait(false);

            Log.Debug(project != null
                    ? string.Format("Activating project '{0}' was canceled", project.Location)
                    : "Deactivating currently active project");

            return true;
        }

        [ObsoleteEx(ReplacementTypeOrMember = "IProjectActivationHistoryService", TreatAsErrorFromVersion = "1.0.0", RemoveInVersion = "1.1.0")]
        public IEnumerable<string> GetActivationHistory()
        {
            throw new NotSupportedException();
        }

        private void InitializeProjectRefresher(string projectLocation)
        {
            IProjectRefresher projectRefresher;

            if (!_projectRefreshers.TryGetValue(projectLocation, out projectRefresher) || projectRefresher == null)
            {
                try
                {
                    projectRefresher = _projectRefresherSelector.GetProjectRefresher(projectLocation);

                    if (projectRefresher != null)
                    {
                        Log.Debug("Subscribing to project refresher '{0}'", projectRefresher.GetType().GetSafeFullName());

                        projectRefresher.Updated += OnProjectRefresherUpdated;
                        projectRefresher.Subscribe();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to subscribe to project refresher");
                }

                if (projectRefresher != null)
                {
                    _projectRefreshers[projectLocation] = projectRefresher;
                }
            }
        }

        private void ReleaseProjectRefresher(IProject project)
        {
            IProjectRefresher projectRefresher;

            var location = project.Location;

            if (_projectRefreshers.TryGetValue(location, out projectRefresher) && projectRefresher != null)
            {
                try
                {
                    Log.Debug("Unsubscribing from project refresher '{0}'", projectRefresher.GetType().GetSafeFullName());

                    projectRefresher.Unsubscribe();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to unsubscribe from project refresher");
                }

                projectRefresher.Updated -= OnProjectRefresherUpdated;

                _projectRefreshers.Remove(location);
            }
        }

        private void OnProjectRefresherUpdated(object sender, ProjectEventArgs e)
        {
            // TODO: use dictionary for detecting if e.Project is currently loading or saving
            if (_isLoading || _savingCounter > 0)
            {
                return;
            }

            ProjectRefreshRequiredAsync.SafeInvoke(this, e);
        }
        #endregion
    }
}