﻿using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public interface IGestorClientes
    {
        Task<RespuestaNifNombreCliente> ComprobarNifNombre(string nif, string nombre);
        Task<RespuestaDatosGeneralesClientes> ComprobarDatosGenerales(string direccion, string codigoPostal, string telefono);
        RespuestaDatosBancoCliente ComprobarDatosBanco(string formaPago, string plazosPago, string iban);
    }
}
