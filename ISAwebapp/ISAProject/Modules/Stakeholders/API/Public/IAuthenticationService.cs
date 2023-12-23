﻿using FluentResults;
using ISAProject.Modules.Stakeholders.API.Dtos;

namespace ISAProject.Modules.Stakeholders.API.Public
{
    public interface IAuthenticationService
    {
        Result<AuthenticationTokensDto> Login(CredentialsDto credentials);
        Result<AuthenticationTokensDto> RegisterUser(EmployeeRegistrationDto account);
        Result<CredentialsDto> RegisterSysAdmin(SysAdminRegistrationDto account);

    }
}
