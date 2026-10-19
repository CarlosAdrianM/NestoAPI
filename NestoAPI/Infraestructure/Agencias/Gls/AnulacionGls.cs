using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Gls
{
    /// <summary>NestoAPI#552 (fase 3): la respuesta de la operación Anula de GLS.</summary>
    public class ResultadoAnulaGls
    {
        /// <summary>Resultado return="0": GLS ha anulado el envío.</summary>
        public bool Exito { get; set; }

        /// <summary>El atributo return de Resultado (null si la respuesta no llega a tenerlo).</summary>
        public string CodigoRetorno { get; set; }

        /// <summary>Lo que dice GLS del fallo, tal cual (null si no lo dice o si ha ido bien).</summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// NestoAPI#552 (fase 3): lectura de la respuesta de Anula. Se lee igual que la de GrabaServicios (un nodo Envio con su
    /// Resultado return="…"): 0 es éxito y cualquier otro código es que GLS no lo ha anulado. A propósito NO hay tabla de
    /// códigos inventada: el motivo es el texto que mande GLS (atributo result del Resultado, Errores/Error o el texto del
    /// Resultado) y, si no manda ninguno, el código tal cual.
    /// </summary>
    public static class RespuestaAnulaGls
    {
        public static ResultadoAnulaGls Interpretar(string respuesta)
        {
            XElement envio = RespuestaGrabaServiciosGls.LeerNodoEnvio(respuesta, PeticionGls.OPERACION_ANULA, out string errorLectura);
            if (envio == null)
            {
                return new ResultadoAnulaGls { Error = errorLectura };
            }

            XElement resultado = RespuestaGrabaServiciosGls.Hijo(envio, "Resultado");
            string retorno = resultado?.Attribute("return")?.Value?.Trim();
            if (retorno == null)
            {
                return new ResultadoAnulaGls { Error = "Respuesta de GLS con formato inesperado: falta Resultado/return." };
            }
            if (retorno == "0")
            {
                return new ResultadoAnulaGls { Exito = true, CodigoRetorno = retorno };
            }

            string motivo = RespuestaGrabaServiciosGls.Vacio(resultado.Attribute("result")?.Value)
                ?? RespuestaGrabaServiciosGls.Vacio(RespuestaGrabaServiciosGls.Hijo(envio, "Errores")?.Elements().FirstOrDefault(e => e.Name.LocalName == "Error")?.Value)
                ?? RespuestaGrabaServiciosGls.Vacio(resultado.Value);
            return new ResultadoAnulaGls { CodigoRetorno = retorno, Error = motivo };
        }
    }

    /// <summary>
    /// NestoAPI#552 (fase 3): la estrategia de GLS para anular un envío ya grabado en GLS, por su código de barras. Es lo
    /// que POST api/EnviosAgencias/{id}/Anular usa a través de <see cref="IAnulacionAgenciaRemota"/>, igual que con CTT e
    /// Innovatrans: el núcleo (estados que se pueden anular, reembolso pagado, BD después de la agencia, auditoría) es el
    /// mismo para todas; aquí solo va lo que es de GLS (petición Anula, cuenta del envío y lectura de la respuesta).
    ///
    /// La cuenta (uidcliente) tiene que ser la misma con la que se grabó, y depende del servicio del envío (BusinessParcel
    /// tiene la suya, <see cref="PeticionGls.Identificador"/>); como la anulación solo recibe el albarán, la busca
    /// <c>uidDelAlbaran</c> (en producción, en la BD: <see cref="UidDesdeLaBD"/>).
    /// </summary>
    public class AnulacionGls : IAnulacionAgenciaRemota
    {
        private readonly IClienteSoapGls _cliente;
        private readonly Func<string, Task<string>> _uidDelAlbaran;
        private readonly RegistroIntercambiosRemotos _registro = new RegistroIntercambiosRemotos();

        public AnulacionGls(IClienteSoapGls cliente, Func<string, Task<string>> uidDelAlbaran)
        {
            _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
            _uidDelAlbaran = uidDelAlbaran ?? throw new ArgumentNullException(nameof(uidDelAlbaran));
        }

        /// <summary>El transporte de producción: la operación Anula del WS b2b, como GrabaServicios.</summary>
        public static IClienteSoapGls ClientePorDefecto() => new ClienteSoapGls(PeticionGls.OPERACION_ANULA, PeticionGls.URL_ANULA);

        public IReadOnlyList<IntercambioRemoto> Intercambios => _registro.Intercambios;

        public async Task<ResultadoOperacionRemota> AnularAsync(string albaran)
        {
            if (string.IsNullOrWhiteSpace(albaran)) throw new ArgumentNullException(nameof(albaran));
            string codigo = albaran.Trim();

            string uid = await _uidDelAlbaran(codigo).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(uid))
            {
                // Sin la cuenta no se llama: con otra, GLS no encontraría el envío o anularía en una cuenta que no es.
                return new ResultadoOperacionRemota
                {
                    Exito = false,
                    Error = $"No se ha podido anular en GLS el envío con albarán {codigo}: no se sabe con qué cuenta de GLS se grabó. " +
                        "Revise que el envío sea de GLS y que la agencia tenga su identificador."
                };
            }

            string soap = PeticionGls.ConstruirSoap(PeticionGls.ConstruirAnulacion(codigo, uid), PeticionGls.OPERACION_ANULA);
            string respuesta;
            try
            {
                respuesta = await _cliente.EnviarAsync(soap).ConfigureAwait(false) ?? string.Empty;
            }
            catch (AgenciaRemotaException ex)
            {
                // La petición queda auditada aunque GLS no conteste: es lo que hay que mirar para saber si llegó a anular.
                _registro.Registrar(PeticionGls.OPERACION_ANULA, _cliente.Url, soap, "(sin respuesta) " + ex.Message);
                throw;
            }
            _registro.Registrar(PeticionGls.OPERACION_ANULA, _cliente.Url, soap, respuesta);

            ResultadoAnulaGls resultado = RespuestaAnulaGls.Interpretar(respuesta);
            if (resultado.Exito)
            {
                return new ResultadoOperacionRemota { Exito = true };
            }
            return new ResultadoOperacionRemota { Exito = false, Error = MensajeNoAnulado(codigo, resultado) };
        }

        /// <summary>Lo que lee el usuario cuando GLS no anula: el motivo de GLS tal cual y qué puede hacer.</summary>
        internal static string MensajeNoAnulado(string albaran, ResultadoAnulaGls resultado)
        {
            string motivo = resultado?.Error;
            if (string.IsNullOrWhiteSpace(motivo))
            {
                motivo = string.IsNullOrWhiteSpace(resultado?.CodigoRetorno)
                    ? "GLS no ha dicho el motivo"
                    : $"GLS ha contestado con el código {resultado.CodigoRetorno.Trim()} sin decir el motivo";
            }
            return $"GLS no ha anulado el envío con albarán {albaran}: {motivo.Trim().TrimEnd('.')}. " +
                "El envío sigue como estaba. Si ya no se puede anular, pídaselo a GLS por correo o por teléfono.";
        }

        /// <summary>
        /// La cuenta de GLS de un envío, buscándolo por su código de barras en la BD: la de BusinessParcel con el servicio 96
        /// o el Identificador de su agencia (lo mismo que se mandó al grabarlo). Null si no hay envío de GLS con ese código.
        /// </summary>
        public static Func<string, Task<string>> UidDesdeLaBD(NVEntities db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            return async albaran =>
            {
                // Where + ToListAsync (no FirstOrDefaultAsync con predicado): ver el «Limit1» de TramitacionEnviosService.
                int gls = Constantes.Agencias.AGENCIA_GLS;
                EnviosAgencia envio = (await db.EnviosAgencias.AsNoTracking()
                    .Where(e => e.CodigoBarras == albaran && e.Agencia == gls)
                    .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
                if (envio == null)
                {
                    return null;
                }
                int numeroAgencia = envio.Agencia;
                AgenciaTransporte agencia = (await db.AgenciasTransportes.AsNoTracking()
                    .Where(a => a.Numero == numeroAgencia)
                    .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
                // Sin recortar, como lo manda TramitadorGls (y Nesto) al grabar: tiene que ser la misma cuenta.
                return PeticionGls.Identificador(envio, agencia?.Identificador);
            };
        }
    }
}
