﻿using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public interface IServicioGestorClientes
    {
        Task<ClienteDTO> BuscarClientePorNif(string nif);
        Task<RespuestaNifNombreCliente> ComprobarNifNombre(string nif, string nombre);
        Task<RespuestaDatosGeneralesClientes> CogerDatosCodigoPostal(string codigoPostal);
        Task<CCC> PrepararCCC(ClienteCrear clienteDTO);
        Task<Cliente> BuscarCliente(string empresa, string cliente, string contacto);
        Task<Cliente> BuscarCliente(NVEntities db, string empresa, string cliente, string contacto);
        Task<VendedorClienteGrupoProducto> BuscarVendedorGrupo(string empresa, string cliente, string contacto, string grupo);
        Task<CondPagoCliente> BuscarCondicionesPago(string empresa, string cliente, string contacto);
        Task<CCC> BuscarCCC(string empresa, string cliente, string contacto, string ccc);
        Task<List<PersonaContactoCliente>> BuscarPersonasContacto(string empresa, string cliente, string contacto);
        Task<string> CalcularSiguienteContacto(string empresa, string cliente);
    }
}
