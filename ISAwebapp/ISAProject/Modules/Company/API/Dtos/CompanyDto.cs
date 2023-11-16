﻿namespace ISAProject.Modules.Company.API.Dtos
{
    public class CompanyDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public AddressDto Address { get; set; }
    }
}
