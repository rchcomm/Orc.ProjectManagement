﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="FileExistsProjectValidator.cs" company="Orchestra development team">
//   Copyright (c) 2008 - 2014 Orchestra development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace Orc.ProjectManagement
{
    using System.IO;
    using System.Threading.Tasks;

    public class FileExistsProjectValidator : ProjectValidatorBase
    {
        #region IProjectValidator Members
        public override async Task<bool> CanStartLoadingProject(string location)
        {
            return File.Exists(location);
        }
        #endregion
    }
}