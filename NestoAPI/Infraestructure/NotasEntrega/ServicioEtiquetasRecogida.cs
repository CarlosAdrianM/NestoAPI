using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>
    /// NestoAPI#758: la etiqueta de cada bulto que se recoge con una nota de RECOGIDA. La misma plantilla que la del reparto
    /// propio (<see cref="EtiquetaRepartoPropio"/>: logo, cliente, dirección, teléfono) con la cabecera grande «RECOGIDA», el texto
    /// de la nota, «Bulto N/M» y el Code128 <c>NR{Pedido}-{N}</c>. Las de ENTREGA no tienen: llevan la RP del packing.
    /// </summary>
    public static class EtiquetaRecogidaNotaEntrega
    {
        public const string TITULO = "RECOGIDA";
        internal const int ALTO_TITULO = 56;

        /// <summary>Los códigos de los bultos: NR{pedido}-1 … NR{pedido}-{bultos}.</summary>
        public static List<string> Codigos(int pedido, int bultos)
            => Enumerable.Range(1, Math.Max(1, bultos)).Select(n => CodigoNotaEntrega.BultoRecogida(pedido, n)).ToList();

        /// <param name="destino">Nombre, dirección, teléfonos y, en Observaciones, el texto de la nota.</param>
        public static string ConstruirZpl(DatosEnvioRemoto destino, int pedido, int bultos, DateTime fecha)
        {
            if (destino == null) throw new ArgumentNullException(nameof(destino));
            if (pedido <= 0) throw new ArgumentException("Falta el número de la nota de recogida.", nameof(pedido));
            var plantilla = new PlantillaEtiquetaRepartoPropio
            {
                Titulo = TITULO,
                AltoTitulo = ALTO_TITULO,
                Referencia = pedido.ToString(CultureInfo.InvariantCulture),
                Bultos = Math.Max(1, bultos),
                CodigoBulto = n => CodigoNotaEntrega.BultoRecogida(pedido, n),
                Pie = "Nueva Visión - Nota de recogida " + pedido.ToString(CultureInfo.InvariantCulture)
            };
            // Una recogida nunca cobra nada (pregunta 7 de la épica): sin recuadro de reembolso.
            destino.Reembolso = 0m;
            return EtiquetaRepartoPropio.ConstruirZpl(destino, fecha, plantilla);
        }
    }

    /// <summary>NestoAPI#758: el resultado de imprimir las etiquetas de recogida (el controlador lo traduce a 200/400/404/502).</summary>
    public class ResultadoEtiquetasRecogida
    {
        public TipoResultadoNotaEntrega Tipo { get; private set; }
        public string Mensaje { get; private set; }
        public EtiquetasRecogidaResultadoDTO Contenido { get; private set; }

        public static ResultadoEtiquetasRecogida Ok(EtiquetasRecogidaResultadoDTO contenido)
            => new ResultadoEtiquetasRecogida { Tipo = TipoResultadoNotaEntrega.Ok, Contenido = contenido };
        public static ResultadoEtiquetasRecogida NoEncontrado(string mensaje)
            => new ResultadoEtiquetasRecogida { Tipo = TipoResultadoNotaEntrega.NoEncontrado, Mensaje = mensaje };
        public static ResultadoEtiquetasRecogida Invalido(string mensaje)
            => new ResultadoEtiquetasRecogida { Tipo = TipoResultadoNotaEntrega.Invalido, Mensaje = mensaje };
        public static ResultadoEtiquetasRecogida FalloImpresora(string mensaje)
            => new ResultadoEtiquetasRecogida { Tipo = TipoResultadoNotaEntrega.FalloImpresora, Mensaje = mensaje };
    }

    /// <summary>
    /// NestoAPI#758: POST api/NotasEntrega/{empresa}/{pedido}/EtiquetasRecogida. Es un servicio de la API (no de Nesto) porque las
    /// etiquetas se irán sacando también desde el packing de Ariadna; Facturar rutas lo lanza solo para cada recogida de reparto
    /// propio. Sale por la impresora de etiquetas de agencia del usuario (<see cref="ImpresorasAgencia"/>, la Zebra de la 14).
    /// </summary>
    public interface IServicioEtiquetasRecogida
    {
        /// <param name="impresora">La que se pida; sin ella, la ImpresoraAgencia del usuario (o la de «(defecto)»).</param>
        Task<ResultadoEtiquetasRecogida> Imprimir(string empresa, int pedido, string impresora, string usuario);
    }

    public class ServicioEtiquetasRecogida : IServicioEtiquetasRecogida
    {
        private readonly IRepositorioNotasEntregaCabecera cabeceras;
        private readonly IRepositorioParadasNotaEntrega paradas;
        private readonly ILectorParametrosUsuario parametros;
        private readonly IImpresoraEtiquetas impresoraEtiquetas;
        private readonly Func<DateTime> hoy;

        public ServicioEtiquetasRecogida(NVEntities db)
            : this(new RepositorioNotasEntregaCabecera(db), new RepositorioParadasNotaEntrega(db), new LectorParametrosUsuario(),
                  new ImpresoraEtiquetasWindows())
        {
        }

        public ServicioEtiquetasRecogida(IRepositorioNotasEntregaCabecera cabeceras, IRepositorioParadasNotaEntrega paradas,
            ILectorParametrosUsuario parametros, IImpresoraEtiquetas impresoraEtiquetas, Func<DateTime> hoy = null)
        {
            this.cabeceras = cabeceras ?? throw new ArgumentNullException(nameof(cabeceras));
            this.paradas = paradas ?? throw new ArgumentNullException(nameof(paradas));
            this.parametros = parametros ?? throw new ArgumentNullException(nameof(parametros));
            this.impresoraEtiquetas = impresoraEtiquetas ?? throw new ArgumentNullException(nameof(impresoraEtiquetas));
            this.hoy = hoy ?? (() => DateTime.Today);
        }

        public async Task<ResultadoEtiquetasRecogida> Imprimir(string empresa, int pedido, string impresora, string usuario)
        {
            empresa = empresa?.Trim();
            PedidoNotaEntrega cabecera = await cabeceras.LeerPedido(empresa, pedido).ConfigureAwait(false);
            if (cabecera == null)
            {
                return ResultadoEtiquetasRecogida.NoEncontrado($"No existe el pedido {pedido}.");
            }
            if (!cabecera.NotaEntrega)
            {
                return ResultadoEtiquetasRecogida.Invalido($"El pedido {pedido} no es una nota de entrega: no lleva etiquetas de recogida.");
            }
            FilaNotaEntregaCabecera fila = await cabeceras.Leer(empresa, pedido).ConfigureAwait(false);
            if (TiposNotaEntrega.Normalizar(fila?.Tipo) != TiposNotaEntrega.RECOGIDA)
            {
                return ResultadoEtiquetasRecogida.Invalido($"La nota {pedido} es de entrega, no de recogida: sus bultos llevan la etiqueta del packing.");
            }
            int bultos = Math.Max(1, (int)fila.Bultos);

            DatosEnvioRemoto destino = await Destino(empresa, pedido).ConfigureAwait(false);
            if (destino == null)
            {
                return ResultadoEtiquetasRecogida.Invalido($"No se encuentra la ficha del cliente de la nota {pedido}: no se pueden hacer sus etiquetas.");
            }

            string destinoImpresora = impresora?.Trim();
            if (string.IsNullOrEmpty(destinoImpresora))
            {
                try
                {
                    destinoImpresora = ServicioEtiquetasHueco.ImpresoraDelUsuario(parametros, Constantes.Empresas.EMPRESA_POR_DEFECTO, usuario,
                        ImpresorasAgencia.Parametro(Constantes.Agencias.AGENCIA_REPARTO_PROPIO));
                }
                catch (ArgumentException ex)
                {
                    return ResultadoEtiquetasRecogida.Invalido(ex.Message);
                }
            }

            string zpl = EtiquetaRecogidaNotaEntrega.ConstruirZpl(destino, pedido, bultos, hoy().Date);
            try
            {
                impresoraEtiquetas.Imprimir(destinoImpresora, zpl, $"Etiquetas de recogida {pedido}");
            }
            catch (ImpresionEtiquetasException ex)
            {
                return ResultadoEtiquetasRecogida.FalloImpresora($"No se han podido imprimir las etiquetas de la recogida {pedido} en {destinoImpresora}: {ex.Message}");
            }

            return ResultadoEtiquetasRecogida.Ok(new EtiquetasRecogidaResultadoDTO
            {
                Impresas = bultos,
                Codigos = EtiquetaRecogidaNotaEntrega.Codigos(pedido, bultos),
                Impresora = destinoImpresora
            });
        }

        /// <summary>El destino de su parada (agencia 14) si ya la tiene; si no, la ficha del cliente con el texto de la nota.</summary>
        private async Task<DatosEnvioRemoto> Destino(string empresa, int pedido)
        {
            List<EnvioPedidoNota> envios = await paradas.LeerEnviosDelPedido(pedido).ConfigureAwait(false) ?? new List<EnvioPedidoNota>();
            EnvioPedidoNota parada = envios.FirstOrDefault(e => e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO
                && (e.Estado == Constantes.Agencias.ESTADO_TRAMITADO || e.Estado == Constantes.Agencias.ESTADO_INCIDENTADO));
            if (parada != null)
            {
                EnviosAgencia envio = await paradas.LeerEnvio(parada.Numero).ConfigureAwait(false);
                if (envio != null)
                {
                    return DatosEnvioRemoto.DesdeEnvio(envio);
                }
            }
            DestinoNotaEntrega ficha = await paradas.LeerDestinoDelPedido(empresa, pedido).ConfigureAwait(false);
            if (ficha == null)
            {
                return null;
            }
            var telefono = new Telefono(ficha.Telefonos);
            return new DatosEnvioRemoto
            {
                Referencia = pedido.ToString(CultureInfo.InvariantCulture),
                Nombre = ficha.Nombre,
                Direccion = ficha.Direccion,
                CodigoPostal = ficha.CodPostal,
                Poblacion = ficha.Poblacion,
                Telefono = telefono.FijoUnico(),
                Movil = telefono.MovilUnico(),
                Observaciones = ficha.Texto
            };
        }
    }
}
