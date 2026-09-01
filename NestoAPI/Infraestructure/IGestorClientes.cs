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
        // pais (Nesto#436): ISO-2 del país de la DIRECCIÓN; para país != ES no se valida contra la
        // tabla española de CPs ni se pasa por el geocoding de España.
        Task<RespuestaDatosGeneralesClientes> ComprobarDatosGenerales(string direccion, string codigoPostal, string telefono, bool direccionVerificada = false, string pais = null);
        RespuestaDatosBancoCliente ComprobarDatosBanco(string formaPago, string plazosPago, string iban);
        Task<ClienteCrear> ConstruirClienteCrear(string empresa, string cliente, string contacto);
        Task<List<PersonaContactoDTO>> LeerPersonasContacto(string empresa, string cliente, string contacto);
        Task<List<EstadoCCCDTO>> LeerEstadosCCC(string empresa);
        Task<GuardarCCCsRespuesta> GuardarCCCs(NVEntities db, GuardarCCCsRequest peticion, string usuario);
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
        Task<ResultadoCopiaDatosPrincipal> CopiarDatosDelPrincipal(string empresa, string cliente, string contactoDestino, string usuario);
        Task<List<ClienteDTO>> BuscarClientesPorTelefono(string telefono);
        Task<List<ClienteDTO>> BuscarClientesPorNif(string nif);
        Task PublicarClienteSincronizar(Cliente cliente, string source = "Nesto", string usuario = null);
    }
}
