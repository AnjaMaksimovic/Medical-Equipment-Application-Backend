﻿using FluentResults;
using ISAProject.Modules.Company.API.Dtos;
using ISAProject.Modules.Stakeholders.API.Dtos;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ISAProject.Modules.Company.API.Public
{
    public interface IContractService
    {
        Result<HospitalContractDto> Create(HospitalContractDto contract);

        Result<HospitalContractDto> Update(HospitalContractDto contract);
    }
}
