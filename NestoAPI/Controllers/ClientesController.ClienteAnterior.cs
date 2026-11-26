using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Clientes.CambioFiscal;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Controllers
{
    /// <summary>Lo que decide el alta sobre el cliente anterior (NestoAPI#876): el 400 si no vale y el aviso del mismo NIF.</summary>
    internal sealed class ValidacionClienteAnterior
    {
        public string Error { get; set; }
        public string Aviso { get; set; }
        /// <summary>El número anterior que se guardará tras el alta (null = nada que guardar).</summary>
        public string Anterior { get; set; }
    }

    public partial class ClientesController
    {
        /// <summary>NestoAPI#876: dbo.ClientesAnteriores (sustituible en los tests).</summary>
        internal IRepositorioClientesAnteriores RepositorioClientesAnteriores { get; set; } = new RepositorioClientesAnterioresSql();

        /// <summary>
        /// NestoAPI#876 corte 1: con origen CAMBIO-FISCAL el alta lleva el número de cliente anterior (<see cref="ReglaClienteAnterior"/>).
        /// Sin la tabla (sin Scripts/Issue876_ClientesAnteriores.sql) no se exige ni se guarda nada: el alta, como hoy. Nunca lanza:
        /// si no se puede leer, el alta sigue sin enlace (ELMAH).
        /// </summary>
        internal async Task<ValidacionClienteAnterior> ValidarClienteAnteriorAlta(ClienteCrear clienteCrear)
        {
            var resultado = new ValidacionClienteAnterior();
            string anterior = string.IsNullOrWhiteSpace(clienteCrear?.ClienteAnterior) ? null : clienteCrear.ClienteAnterior.Trim();
            // Corte de la copia por bloques: CopiarDelAnterior solo con el anterior y con bloques que existen (puro, sin leer nada)
            if (clienteCrear?.CopiarDelAnterior != null)
            {
                if (anterior == null)
                {
                    resultado.Error = "Para copiar las condiciones del cliente anterior, diga cuál es (ClienteAnterior).";
                    return resultado;
                }
                _ = BloquesCambioFiscal.Normalizar(clienteCrear.CopiarDelAnterior, out System.Collections.Generic.List<string> desconocidos);
                if (desconocidos.Count > 0)
                {
                    resultado.Error = $"No existe el bloque {string.Join(", ", desconocidos)}. Los bloques son: {string.Join(", ", BloquesCambioFiscal.TODOS)}.";
                    return resultado;
                }
            }
            if (clienteCrear == null || (anterior == null && !ReglaClienteAnterior.EsCambioFiscal(clienteCrear.Origen)))
            {
                return resultado;
            }
            try
            {
                if (!RepositorioClientesAnteriores.TablaExiste())
                {
                    return resultado;
                }
                string empresa = EmpresaODefecto(clienteCrear.Empresa);
                DatosClienteAnterior datos = anterior == null ? null
                    : await RepositorioClientesAnteriores.LeerCliente(empresa, anterior).ConfigureAwait(false);
                resultado.Error = ReglaClienteAnterior.MotivoNoValido(clienteCrear.Origen, anterior, clienteCrear.EsContacto ? clienteCrear.Cliente : null,
                    empresa, datos);
                if (resultado.Error == null)
                {
                    resultado.Anterior = anterior;
                    resultado.Aviso = ReglaClienteAnterior.AvisoMismoNif(datos, clienteCrear.Nif);
                }
            }
            catch (Exception ex)
            {
                RegistrarErrorOrigen?.Invoke(new Exception($"NestoAPI#876: no se ha podido comprobar el cliente anterior {anterior}: {ex.Message}", ex));
                return new ValidacionClienteAnterior();
            }
            return resultado;
        }

        /// <summary>NestoAPI#876: guarda el enlace DESPUÉS del alta (otra tabla, como el origen). Si falla, el cliente queda dado de alta (ELMAH).</summary>
        internal async Task GuardarClienteAnteriorTrasAlta(Cliente cliente, ValidacionClienteAnterior validacion)
        {
            if (cliente == null || string.IsNullOrWhiteSpace(validacion?.Anterior))
            {
                return;
            }
            string nuevo = cliente.Nº_Cliente?.Trim();
            if (string.Equals(nuevo, validacion.Anterior, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            try
            {
                await RepositorioClientesAnteriores.Guardar(EmpresaODefecto(cliente.Empresa), nuevo, validacion.Anterior, User?.Identity?.Name).ConfigureAwait(false);
                cliente.ClienteAnterior = validacion.Anterior;
                cliente.AvisoClienteAnterior = validacion.Aviso;
            }
            catch (Exception ex)
            {
                RegistrarErrorOrigen?.Invoke(new Exception(
                    $"NestoAPI#876: el cliente {nuevo} se ha dado de alta, pero no su cliente anterior {validacion.Anterior}: {ex.Message}", ex));
            }
        }

        /// <summary>NestoAPI#876: el número anterior para la ficha. Nunca lanza: null si no tiene, no hay tabla o no se puede leer.</summary>
        internal async Task<string> LeerClienteAnteriorSeguro(string empresa, string cliente)
        {
            try
            {
                return string.IsNullOrWhiteSpace(cliente) || !RepositorioClientesAnteriores.TablaExiste() ? null
                    : await RepositorioClientesAnteriores.LeerAnterior(EmpresaODefecto(empresa), cliente.Trim()).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
