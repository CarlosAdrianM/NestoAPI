using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System.Collections.Generic;
using System.Data.Entity;
using System.Net.Http;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public interface IGestorClientes
    {
        Task<RespuestaNifNombreCliente> ComprobarNifNombre(string nif, string nombre);
        Task<RespuestaDatosGeneralesClientes> ComprobarDatosGenerales(string direccion, string codigoPostal, string telefono);
        RespuestaDatosBancoCliente ComprobarDatosBanco(string formaPago, string plazosPago, string iban);
        Task<ClienteCrear> ConstruirClienteCrear(string empresa, string cliente, string contacto);
        Task<Cliente> PrepararClienteCrear(ClienteCrear clienteCrear, NVEntities db);
        Task<Cliente> PrepararClienteModificar(ClienteCrear clienteModificar, NVEntities db);
        Task<List<Cliente>> DejarDeVisitar(NVEntities db, ClienteCrear cliente);
        Task<List<ClienteProbabilidadVenta>> BuscarClientesPorProbabilidadVenta(string vendedor, int numeroClientes, string tipoInteraccion, string subgrupo = "");
        Task<Cliente> ModificarCliente(ClienteCrear clienteCrear, NVEntities db);
        Task<Cliente> CrearCliente(ClienteCrear clienteCrear, NVEntities db);
        Task<Mandato> LeerMandato(string empresa, string cliente, string contacto, string ccc);
        ByteArrayContent MandatoEnPDF(List<Mandato> mandatos);
        Task<ClienteTelefonoLookup> BuscarClientePorEmail(string email);
        Task<ClienteDTO> BuscarClientePorEmailNif(string email, string nif);
        Task PublicarClienteSincronizar(Cliente cliente, string source = "Nesto");
    }
}
