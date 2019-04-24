using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;

namespace NestoAPI.Infraestructure
{
    public class GestorClientes : IGestorClientes
    {
        IServicioGestorClientes servicio;
        public GestorClientes()
        {
            servicio = new ServicioGestorClientes();
        }
        public GestorClientes(IServicioGestorClientes servicio)
        {
            this.servicio = servicio;
        }

        public async Task<RespuestaNifNombreCliente> ComprobarNifNombre(string nif, string nombre)
        {
            if (String.IsNullOrWhiteSpace(nombre))
            {
                throw new ArgumentException("El nombre no puede estar en blanco");
            }
            var respuesta = new RespuestaNifNombreCliente();
            if (String.IsNullOrWhiteSpace(nif))
            {
                respuesta.EstadoCliente = Constantes.Clientes.Estados.PRIMERA_VISITA;
                return respuesta;
            }
            ClienteDTO clienteEncontrado = await servicio.BuscarClientePorNif(nif);
            if(clienteEncontrado != null && clienteEncontrado.cliente != null)
            {
                respuesta.ExisteElCliente = true;
                respuesta.NombreFormateado = clienteEncontrado.nombre;
                respuesta.NifFormateado = clienteEncontrado.cifNif;
                respuesta.NifValidado = true;
            } else
            {
                respuesta = await servicio.ComprobarNifNombre(nif, nombre);
                respuesta.ExisteElCliente = false;
            }
            return respuesta;
        }

    }
}