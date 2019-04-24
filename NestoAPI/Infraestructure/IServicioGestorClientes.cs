using NestoAPI.Models;
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
    }
}
