﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ProjectManager.cs" company="WildGums">
//   Copyright (c) 2008 - 2015 WildGums. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

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
    using Catel.Threading;

    internal class ProjectManager : IProjectManager, INeedCustomInitialization
    {
        #region Fields
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();
        private readonly IProjectInitializer _projectInitializer;
        private readonly IProjectManagementInitializationService _projectManagementInitializationService;
        private readonly IProjectStateService _projectStateService;
        private readonly IDictionary<string, IProjectRefresher> _projectRefreshers;
        private readonly IProjectRefresherSelector _projectRefresherSelector;
        private readonly ListDictionary<string, IProject> _projects;
        private readonly IProjectSerializerSelector _projectSerializerSelector;
        private readonly IProjectValidator _projectValidator;
        private readonly IProjectUpgrader _projectUpgrader;
        
        private readonly AsyncLock _asyncLoadLock = new AsyncLock();
        private readonly AsyncLock _asyncActivateLock = new AsyncLock();

        private int _savingCounter;
        #endregion

        #region Constructors
        public ProjectManager(IProjectValidator projectValidator, IProjectUpgrader projectUpgrader, IProjectRefresherSelector projectRefresherSelector, 
            IProjectSerializerSelector projectSerializerSelector, IProjectInitializer projectInitializer, IProjectManagementConfigurationService projectManagementConfigurationService,
            IProjectManagementInitializationService projectManagementInitializationService, IProjectStateService projectStateService)
        {
            Argument.IsNotNull(() => projectValidator);
            Argument.IsNotNull(() => projectUpgrader);
            Argument.IsNotNull(() => projectRefresherSelector);
            Argument.IsNotNull(() => projectSerializerSelector);
            Argument.IsNotNull(() => projectInitializer);
            Argument.IsNotNull(() => projectManagementConfigurationService);
            Argument.IsNotNull(() => projectManagementInitializationService);
            Argument.IsNotNull(() => projectStateService);

            _projectValidator = projectValidator;
            _projectUpgrader = projectUpgrader;
            _projectRefresherSelector = projectRefresherSelector;
            _projectSerializerSelector = projectSerializerSelector;
            _projectInitializer = projectInitializer;
            _projectManagementInitializationService = projectManagementInitializationService;
            _projectStateService = projectStateService;

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

        public bool IsLoading { get; private set; }
        #endregion

        #region Methods
        void INeedCustomInitialization.Initialize()
        {
            _projectManagementInitializationService.Initialize(this);
        }
        #endregion

        #region Events
        [ObsoleteEx(Message = "Won't be replaced", TreatAsErrorFromVersion = "1.0", RemoveInVersion = "2.0")]
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
            var locations = _projectInitializer.GetInitialLocations().Where(x => !string.IsNullOrWhiteSpace(x));

            foreach (var location in locations)
            {
                Log.Debug("Loading initial project from location '{0}'", location);
                await LoadAsync(location).ConfigureAwait(false);
            }
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

            var cancelEventArgs = new ProjectCancelEventArgs(project);

            await ProjectRefreshingAsync.SafeInvokeAsync(this, cancelEventArgs, false).ConfigureAwait(false);

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

                validationContext = await _projectValidator.ValidateProjectBeforeLoadingAsync(projectLocation);
                if (validationContext.HasErrors)
                {
                    throw Log.ErrorAndCreateException<InvalidOperationException>($"Project could not be loaded from '{projectLocation}', the validator returned errors");
                }

                var loadedProject = await QuietlyLoadProjectAsync(projectLocation, false).ConfigureAwait(false);

                validationContext = await _projectValidator.ValidateProjectAsync(loadedProject);
                if (validationContext.HasErrors)
                {
                    throw Log.ErrorAndCreateException<InvalidOperationException>($"Project data was loaded from '{projectLocation}', but the validator returned errors");
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
                new ProjectException(project, $"Failed to load project from location '{projectLocation}' while refreshing.", error),
                validationContext);

            await ProjectRefreshingFailedAsync.SafeInvokeAsync(this, eventArgs).ConfigureAwait(false);

            return false;
        }

        public virtual async Task<bool> LoadAsync(string location)
        {
            Argument.IsNotNullOrWhitespace("location", location);

            var project = await LoadProjectAsync(location).ConfigureAwait(false);

            if (project != null)
            {
                await SetActiveProjectAsync(project).ConfigureAwait(false);
            }

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

            IProject project;
            using (await _asyncLoadLock.LockAsync())
            {
                project = Projects.FirstOrDefault(x => string.Equals(location, x.Location, StringComparison.OrdinalIgnoreCase));
                if (project != null)
                {
                    return project;
                }

                using (new DisposableToken(null, token => IsLoading = true, token => IsLoading = false))
                {
                    Log.Debug($"Going to load project from '{location}', checking if an upgrade is required");

                    if (await _projectUpgrader.RequiresUpgradeAsync(location))
                    {
                        Log.Debug($"Upgrade is required for '{location}', upgrading...");

                        location = await _projectUpgrader.UpgradeAsync(location);

                        Log.Debug($"Upgraded project, final location is '{location}'");
                    }

                    Log.Debug($"Loading project from '{location}'");

                    var cancelEventArgs = new ProjectCancelEventArgs(location);

                    await ProjectLoadingAsync.SafeInvokeAsync(this, cancelEventArgs, false).ConfigureAwait(false);

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
                            throw Log.ErrorAndCreateException<SdiProjectManagementException>("Cannot load project '{0}', currently in SDI mode", location);
                        }

                        if (!await _projectValidator.CanStartLoadingProjectAsync(location))
                        {
                            validationContext = new ValidationContext();
                            validationContext.AddBusinessRuleValidationResult(BusinessRuleValidationResult.CreateError("Project validator informed that project could not be loaded"));

                            throw new ProjectException(location, $"Cannot load project from '{location}'");
                        }

                        validationContext = await _projectValidator.ValidateProjectBeforeLoadingAsync(location);
                        if (validationContext.HasErrors)
                        {
                            throw Log.ErrorAndCreateException<InvalidOperationException>($"Project could not be loaded from '{location}', validator returned errors");
                        }

                        project = await QuietlyLoadProjectAsync(location, true).ConfigureAwait(false);

                        validationContext = await _projectValidator.ValidateProjectAsync(project);
                        if (validationContext.HasErrors)
                        {
                            throw Log.ErrorAndCreateException<InvalidOperationException>($"Project data was loaded from '{location}', but the validator returned errors");
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
            }

            return project;
        }

        private void RegisterProject(IProject project)
        {
            var projectLocation = project.Location;
            _projects[projectLocation] = project;

            InitializeProjectRefresher(projectLocation);
        }

        private async Task<IProject> QuietlyLoadProjectAsync(string location, bool skipCanLoadValidation)
        {
            if (skipCanLoadValidation)
            {
                Log.Debug("Validating to see if we can load the project from '{0}'", location);

                if (!await _projectValidator.CanStartLoadingProjectAsync(location))
                {
                    throw new ProjectException(location, $"Cannot load project from '{location}'");
                }
            }

            var projectReader = _projectSerializerSelector.GetReader(location);
            if (projectReader == null)
            {
                throw Log.ErrorAndCreateException<InvalidOperationException>($"No project reader is found for location '{location}'");
            }

            Log.Debug("Using project reader '{0}'", projectReader.GetType().Name);

            var project = await projectReader.ReadAsync(location).ConfigureAwait(false);
            if (project == null)
            {
                throw Log.ErrorAndCreateException<InvalidOperationException>($"Project could not be loaded from '{location}'");
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
                await ProjectSavingAsync.SafeInvokeAsync(this, cancelEventArgs, false).ConfigureAwait(false);

                if (cancelEventArgs.Cancel)
                {
                    Log.Debug("Canceled saving of project to '{0}'", location);
                    await ProjectSavingCanceledAsync.SafeInvokeAsync(this, new ProjectEventArgs(project)).ConfigureAwait(false);
                    return false;
                }

                var projectWriter = _projectSerializerSelector.GetWriter(location);
                if (projectWriter == null)
                {
                    throw Log.ErrorAndCreateException<NotSupportedException>($"No project writer is found for location '{location}'");
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
            await ProjectClosingAsync.SafeInvokeAsync(this, cancelEventArgs, false).ConfigureAwait(false);

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
            using (await _asyncActivateLock.LockAsync())
            {                
                if(project == null)
                {
                    return await DeactivateActiveProjectAsync();
                }

                var activeProject = ActiveProject;

                if (!Projects.Contains(project))
                {
                    Log.Warning($"Unable to activate it project '{project.Location}' because it does not exists in the list of known projects");
                    return false;
                }

                var activeProjectLocation = activeProject?.Location;
                var newProjectLocation = project?.Location;

                if (string.Equals(activeProjectLocation, newProjectLocation))
                {
                    Log.Debug($"The project '{newProjectLocation ?? string.Empty}' is already active, no need to activate it again");
                    return false;
                }

                Log.Info($"Activating project '{project.Location}'");

                var eventArgs = new ProjectUpdatingCancelEventArgs(activeProject, project);

                await ProjectActivationAsync.SafeInvokeAsync(this, eventArgs, false).ConfigureAwait(false);

                if (eventArgs.Cancel)
                {
                    Log.Info($"Activating project '{project.Location}' was canceled");

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
                        ? $"Failed to activate project '{project.Location}'"
                        : "Failed to deactivate currently active project");

                    await ProjectActivationFailedAsync.SafeInvokeAsync(this, new ProjectErrorEventArgs(project, exception)).ConfigureAwait(false);
                    return false;
                }

                await ProjectActivatedAsync.SafeInvokeAsync(this, new ProjectUpdatedEventArgs(activeProject, project)).ConfigureAwait(false);

                Log.Debug(project != null
                    ? $"Activating project '{project.Location}' was canceled"
                    : "Deactivating currently active project");
            }

            return true;
        }

        private async Task<bool> DeactivateActiveProjectAsync()
        {
            var project = ActiveProject;
            var location = project?.Location;

            if (ReferenceEquals(location, null))
            {
                return false;
            }

            Log.Info($"Deactivating project '{location}'");

            var eventArgs = new ProjectUpdatingCancelEventArgs(project, null);
            await ProjectActivationAsync.SafeInvokeAsync(this, eventArgs, false).ConfigureAwait(false);

            _projectStateService.UpdateState(location, state => state.IsDeactivating = true);

            if (eventArgs.Cancel)
            {
                Log.Info($"Deactivating project '{location}' was canceled");

                await ProjectActivationCanceledAsync.SafeInvokeAsync(this, new ProjectEventArgs(project)).ConfigureAwait(false);
                return false;
            }
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
                        Log.Debug("Subscribing to project refresher '{0}'", projectRefresher.GetType().GetSafeFullName(false));

                        projectRefresher.Updated += OnProjectRefresherUpdated;
                        projectRefresher.Subscribe();

                        _projectRefreshers[projectLocation] = projectRefresher;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to subscribe to project refresher");
                    throw;
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
                    Log.Debug("Unsubscribing from project refresher '{0}'", projectRefresher.GetType().GetSafeFullName(false));

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
            if (IsLoading || _savingCounter > 0)
            {
                return;
            }

            ProjectRefreshRequiredAsync.SafeInvoke(this, e);
        }
        #endregion
    }
}