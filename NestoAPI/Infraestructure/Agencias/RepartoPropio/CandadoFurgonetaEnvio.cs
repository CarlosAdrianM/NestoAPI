using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>Quién lleva encima algún bulto de un envío (o de un envío interno) y desde cuándo.</summary>
    public class BultosEnFurgoneta
    {
        /// <summary>El usuario tal cual (con dominio) del primero que se cargó.</summary>
        public string Usuario { get; set; }
        /// <summary>«Andre».</summary>
        public string Repartidor { get; set; }
        /// <summary>La hora (del aparato) del movimiento que dejó en la furgoneta el primero de sus bultos.</summary>
        public DateTime Desde { get; set; }
        /// <summary>Los códigos que siguen en alguna furgoneta.</summary>
        public List<string> Codigos { get; set; } = new List<string>();
    }

    /// <summary>
    /// Carlos 20/11/26: en el reparto propio manda el BULTO. Un bulto está siempre en uno de: almacén sin cargar · furgoneta de un
    /// repartidor · entregado · de vuelta en el almacén; ninguna acción de oficina puede dejar un bulto físico sin parada (envío 251169:
    /// cargado a las 9:42 y «dado por cerrado a mano» a las 9:43; envío 251227: borrado con la etiqueta ya pegada). LA REGLA, pura:
    /// <list type="bullet">
    /// <item><see cref="QuienLoLleva"/>: algún bulto del envío (RP{envío}-{N} o el papel NE/NR de su nota) cuyo último movimiento es
    /// Cargado o Recogido (<see cref="NucleoFurgoneta"/>).</item>
    /// <item>Con eso, desde oficina no se puede dar por cerrado, anular, borrar ni cambiar la agencia o pasar a un estado terminal
    /// (<see cref="EsEstadoTerminal"/>) un envío de la 14: 409 <see cref="CODIGO_EN_FURGONETA"/>. Lo que hace el repartidor en Ariadna
    /// (entregar, incidencia, descargar, recoger) y la red de seguridad no pasan por aquí.</item>
    /// <item>Y «Dar por cerrado» nunca vale para la 14 (<see cref="CODIGO_CIERRE_NO_PERMITIDO"/>).</item>
    /// </list>
    /// </summary>
    public static class CandadoFurgonetaEnvio
    {
        public const string CODIGO_EN_FURGONETA = "EN_FURGONETA";
        public const string CODIGO_CIERRE_NO_PERMITIDO = "CIERRE_NO_PERMITIDO";
        public const string MENSAJE_CIERRE_NO_PERMITIDO = "Un envío del reparto propio no se da por cerrado: se entrega (en Ariadna), " +
            "o se anula con sus bultos de vuelta en el almacén.";

        /// <summary>¿Le aplica el candado? Solo la agencia 14.</summary>
        public static bool Aplica(EnviosAgencia envio) => envio != null && envio.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO;

        /// <summary>Entregado, devuelto o cerrado a mano.</summary>
        public static bool EsEstadoTerminal(short estado)
            => estado == Constantes.Agencias.ESTADO_ENTREGADO || estado == Constantes.Agencias.ESTADO_DEVUELTO || estado == Constantes.Agencias.ESTADO_CERRADO_A_MANO;

        /// <summary>
        /// ¿Este cambio desde oficina (PUT, ModificarDatos) es de los que el candado frena? Cambia la agencia de un envío de la 14, o lo
        /// pasa a un estado terminal desde uno que no lo era.
        /// </summary>
        public static bool CambioQueFrena(EnviosAgencia registrado, int agenciaNueva, short estadoNuevo)
            => Aplica(registrado)
               && (agenciaNueva != registrado.Agencia || (EsEstadoTerminal(estadoNuevo) && estadoNuevo != registrado.Estado));

        /// <summary>
        /// El núcleo: de los movimientos de unos bultos, los que siguen en una furgoneta (último Cargado o Recogido). Null si ninguno.
        /// Lo usan los envíos y los envíos internos.
        /// </summary>
        public static BultosEnFurgoneta EnFurgoneta(IEnumerable<MovimientoBultoReparto> movimientosDeSusBultos)
        {
            List<MovimientoBultoReparto> encima = NucleoFurgoneta.EnFurgoneta(movimientosDeSusBultos);
            if (encima.Count == 0)
            {
                return null;
            }
            MovimientoBultoReparto primero = encima[0];
            return new BultosEnFurgoneta
            {
                Usuario = primero.Usuario?.Trim(),
                Repartidor = ServicioRepartoPropio.NombreRepartidor(primero.Usuario),
                Desde = primero.FechaHora,
                Codigos = encima.Select(m => m.Codigo?.Trim()).ToList()
            };
        }

        /// <param name="historia">Movimientos que pueden ser del envío (por EnvioId o, si es una nota, por su Pedido).</param>
        public static BultosEnFurgoneta QuienLoLleva(int envioId, int? pedido, IEnumerable<MovimientoBultoReparto> historia)
            => EnFurgoneta((historia ?? Enumerable.Empty<MovimientoBultoReparto>())
                .Where(m => m != null && (m.EnvioId == envioId
                    || (pedido.HasValue && m.Pedido == pedido && m.TipoCodigo == CodigoBultoFurgoneta.NR))));

        /// <summary>«las 9:42» si es de hoy; «el 19/11 a las 9:42» si no.</summary>
        public static string CuandoDesde(DateTime desde, DateTime ahora)
            => desde.Date == ahora.Date
                ? "las " + desde.ToString("H:mm", CultureInfo.InvariantCulture)
                : "el " + desde.ToString("dd/MM", CultureInfo.InvariantCulture) + " a las " + desde.ToString("H:mm", CultureInfo.InvariantCulture);

        /// <summary>«No se puede: el envío 251169 lo lleva Andre en la furgoneta desde las 9:42. Que lo descargue o lo entregue primero.»</summary>
        public static string Mensaje(string que, BultosEnFurgoneta furgoneta, DateTime ahora)
        {
            string quien = string.IsNullOrWhiteSpace(furgoneta?.Repartidor) ? "un repartidor" : furgoneta.Repartidor;
            return $"No se puede: {que} lo lleva {quien} en la furgoneta desde {CuandoDesde(furgoneta?.Desde ?? ahora, ahora)}. " +
                "Que lo descargue o lo entregue primero.";
        }

        public static string MensajeEnvio(int envio, BultosEnFurgoneta furgoneta, DateTime ahora)
            => Mensaje("el envío " + envio.ToString(CultureInfo.InvariantCulture), furgoneta, ahora);

        /// <summary>El cuerpo del 409: { error { code, message, details }, Message, Codigo, Repartidor, Desde } (el formato de los 409 del reparto).</summary>
        public static object Cuerpo409(string mensaje, BultosEnFurgoneta furgoneta)
            => new
            {
                error = new { code = CODIGO_EN_FURGONETA, message = mensaje, details = new { Repartidor = furgoneta?.Repartidor, Desde = furgoneta?.Desde, Codigos = furgoneta?.Codigos } },
                Message = mensaje,
                Codigo = CODIGO_EN_FURGONETA,
                Repartidor = furgoneta?.Repartidor,
                Desde = furgoneta?.Desde
            };

        public static object CuerpoCierreNoPermitido()
            => new
            {
                error = new { code = CODIGO_CIERRE_NO_PERMITIDO, message = MENSAJE_CIERRE_NO_PERMITIDO, details = (object)null },
                Message = MENSAJE_CIERRE_NO_PERMITIDO,
                Codigo = CODIGO_CIERRE_NO_PERMITIDO
            };
    }

    /// <summary>Lee si algún bulto de un envío va en una furgoneta. Sustituible en los tests.</summary>
    public interface ILectorFurgonetaEnvio
    {
        /// <summary>Null si ninguno (o sin Scripts/Issue772_FurgonetaBultos.sql).</summary>
        Task<BultosEnFurgoneta> Leer(EnviosAgencia envio);
    }

    public class LectorFurgonetaEnvioSql : ILectorFurgonetaEnvio
    {
        private readonly NVEntities db;

        public LectorFurgonetaEnvioSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<BultosEnFurgoneta> Leer(EnviosAgencia envio)
        {
            if (!CandadoFurgonetaEnvio.Aplica(envio))
            {
                return null;
            }
            var repositorio = new RepositorioFurgoneta(db);
            if (!repositorio.ScriptAplicado())
            {
                return null;
            }
            var consulta = new ConsultaMovimientosBulto
            {
                Envios = new[] { envio.Numero },
                Pedidos = envio.Pedido.HasValue && envio.Pedido.Value > 0 ? new[] { envio.Pedido.Value } : null
            };
            List<MovimientoBultoReparto> historia = await repositorio.LeerHistoria(consulta).ConfigureAwait(false);
            return CandadoFurgonetaEnvio.QuienLoLleva(envio.Numero, envio.Pedido, historia);
        }
    }
}
