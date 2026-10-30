using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>NestoAPI#760: lo que contesta el servicio de abonos de recogida; el controlador lo traduce a 200/201/404/400/409.</summary>
    public class ResultadoAbonoRecogida<T>
    {
        public TipoResultadoNotaEntrega Tipo { get; private set; }
        public string Mensaje { get; private set; }
        public T Contenido { get; private set; }
        /// <summary>201 (el abono se acaba de crear) frente a 200.</summary>
        public bool Creado { get; private set; }
        /// <summary>El código del 409 (<see cref="ServicioAbonosRecogida.CODIGO_NO_RECOGIDA"/>), con sus detalles.</summary>
        public string Codigo { get; private set; }
        public object Detalles { get; private set; }

        public static ResultadoAbonoRecogida<T> Ok(T contenido, bool creado = false)
            => new ResultadoAbonoRecogida<T> { Tipo = TipoResultadoNotaEntrega.Ok, Contenido = contenido, Creado = creado };
        public static ResultadoAbonoRecogida<T> NoEncontrado(string mensaje)
            => new ResultadoAbonoRecogida<T> { Tipo = TipoResultadoNotaEntrega.NoEncontrado, Mensaje = mensaje };
        public static ResultadoAbonoRecogida<T> Invalido(string mensaje)
            => new ResultadoAbonoRecogida<T> { Tipo = TipoResultadoNotaEntrega.Invalido, Mensaje = mensaje };
        public static ResultadoAbonoRecogida<T> Conflicto(string mensaje, string codigo, object detalles)
            => new ResultadoAbonoRecogida<T> { Tipo = TipoResultadoNotaEntrega.Conflicto, Mensaje = mensaje, Codigo = codigo, Detalles = detalles };
    }

    public interface IServicioAbonosRecogida
    {
        /// <summary>GET api/NotasEntrega/Recogidas/{codigo}. NoEncontrado si el código no es de una nota de recogida.</summary>
        Task<ResultadoAbonoRecogida<RecogidaDTO>> Leer(string codigo, bool verDatosPersonales);

        /// <summary>POST api/NotasEntrega/Recogidas/Abono. Idempotente (PedidoAbono de la nota).</summary>
        Task<ResultadoAbonoRecogida<AbonoRecogidaDTO>> CrearAbono(CrearAbonoRecogidaDTO peticion, string usuario);

        /// <summary>
        /// PUT del pedido: si es el abono de una recogida con factura original y alguna línea en negativo es de un producto que no
        /// está en esa factura, el aviso (no bloquea). Null en todos los demás casos. Nunca lanza.
        /// </summary>
        Task<AvisoPedidoDTO> AvisoProductosFueraDeFactura(PedidoVentaDTO pedido);
    }

    /// <summary>
    /// NestoAPI#760 (corte 4 de #755): el repartidor trae el bulto con la etiqueta NR{Pedido}-{N} y Alfredo o Santiago la escanean
    /// en Nesto: se crea solo el PEDIDO DE ABONO (sin líneas) del cliente de la nota, en la serie rectificativa (NV → RV, decisión 8
    /// de la épica), y Nesto lo abre con cantidad −1 y el cursor en Producto. Las líneas van por el PUT de siempre.
    /// <para>Con FacturaOriginal en la nota, al facturar el abono las vinculaciones de la rectificativa salen PRIMERO de esa
    /// factura (<see cref="Rectificativas.GestorFacturasRectificativas"/> con factura preferida) y lo que no esté en ella, por el
    /// LIFO de #38; sin factura original, todo por el LIFO. La factura la hace cualquiera de Almacén (decisión 9), como hoy.</para>
    /// </summary>
    public class ServicioAbonosRecogida : IServicioAbonosRecogida
    {
        public const string CODIGO_NO_RECOGIDA = "RECOGIDA_NO_RECOGIDA";
        public const string TIPO_AVISO_FUERA_DE_FACTURA = "ABONO_PRODUCTO_FUERA_DE_FACTURA";
        public const string TABLA_MODIFICACIONES = "Pedidos";
        public const string ESTADO_SIN_ENVIO = "SinEnvio";
        /// <summary>Las rectificativas, al contado (como Copiar factura, #85: requisito legal).</summary>
        internal const string PLAZOS_ABONO = Constantes.PlazosPago.CONTADO;
        internal const string FORMA_PAGO_POR_DEFECTO = Constantes.FormasPago.EFECTIVO;
        private const int LONGITUD_USUARIO_PEDIDO = 30;

        private readonly IRepositorioAbonosRecogida repositorio;
        private readonly Func<DateTime> hoy;
        private readonly Action<Exception> registrar;

        public ServicioAbonosRecogida(IRepositorioAbonosRecogida repositorio) : this(repositorio, null, null)
        {
        }

        internal ServicioAbonosRecogida(IRepositorioAbonosRecogida repositorio, Func<DateTime> hoy, Action<Exception> registrar)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.hoy = hoy ?? (() => DateTime.Today);
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        public async Task<ResultadoAbonoRecogida<RecogidaDTO>> Leer(string codigo, bool verDatosPersonales)
        {
            NotaResuelta resuelta = await Resolver(codigo).ConfigureAwait(false);
            if (resuelta.Error != null)
            {
                return ResultadoAbonoRecogida<RecogidaDTO>.NoEncontrado(resuelta.Error);
            }
            NotaRecogidaLeida nota = resuelta.Nota;
            ParadaRecogidaLeida parada = await repositorio.LeerParada(nota.Empresa, nota.Pedido).ConfigureAwait(false);
            int? abono = nota.PedidoAbono;
            if (abono.HasValue && await repositorio.LeerPedido(nota.Empresa, abono.Value).ConfigureAwait(false) == null)
            {
                abono = null; // lo han borrado: al leer otra vez la etiqueta se crea uno nuevo
            }
            return ResultadoAbonoRecogida<RecogidaDTO>.Ok(new RecogidaDTO
            {
                Empresa = nota.Empresa,
                PedidoNota = nota.Pedido,
                Cliente = nota.Cliente,
                Contacto = nota.Contacto,
                Nombre = nota.Nombre,
                TextoNota = nota.TextoNota,
                Bultos = nota.Bultos,
                FacturaOriginal = nota.FacturaOriginal,
                EstadoEnvio = ServicioNotasEntregaCabecera.EstadoEnvio(parada?.Estado),
                FechaRecogida = parada?.FechaRecogida,
                Receptor = verDatosPersonales ? parada?.Receptor : null,
                PedidoAbono = abono
            });
        }

        public async Task<ResultadoAbonoRecogida<AbonoRecogidaDTO>> CrearAbono(CrearAbonoRecogidaDTO peticion, string usuario)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Codigo))
            {
                return ResultadoAbonoRecogida<AbonoRecogidaDTO>.Invalido("Lea la etiqueta de la recogida (NR…) o escriba su código.");
            }
            NotaResuelta resuelta = await Resolver(peticion.Codigo).ConfigureAwait(false);
            if (resuelta.Error != null)
            {
                return ResultadoAbonoRecogida<AbonoRecogidaDTO>.NoEncontrado(resuelta.Error);
            }
            NotaRecogidaLeida nota = resuelta.Nota;

            AbonoRecogidaDTO existente = await AbonoExistente(nota).ConfigureAwait(false);
            if (existente != null)
            {
                return ResultadoAbonoRecogida<AbonoRecogidaDTO>.Ok(existente);
            }

            if (!peticion.Forzar)
            {
                ParadaRecogidaLeida parada = await repositorio.LeerParada(nota.Empresa, nota.Pedido).ConfigureAwait(false);
                if (parada == null || parada.Estado != Constantes.Agencias.ESTADO_ENTREGADO)
                {
                    string estado = ServicioNotasEntregaCabecera.EstadoEnvio(parada?.Estado);
                    string comoVa = estado == null ? "no tiene envío de reparto propio" : $"estado {estado}";
                    return ResultadoAbonoRecogida<AbonoRecogidaDTO>.Conflicto(
                        $"La recogida de la nota {nota.Pedido} no figura como recogida ({comoVa}).",
                        CODIGO_NO_RECOGIDA,
                        new { estado = estado ?? ESTADO_SIN_ENVIO, envio = parada?.EnvioId, pedidoNota = nota.Pedido });
                }
            }

            FichaClienteAbono ficha = await repositorio.LeerFicha(nota.Empresa, nota.Cliente, nota.Contacto).ConfigureAwait(false);
            if (ficha == null)
            {
                return ResultadoAbonoRecogida<AbonoRecogidaDTO>.Invalido(
                    $"No se encuentra la ficha del cliente {nota.Cliente}/{nota.Contacto} de la nota {nota.Pedido}.");
            }

            int numero = repositorio.ReservarNumeroPedido();
            CabPedidoVta cabecera = ConstruirCabecera(nota, ficha, numero, hoy(), usuario);
            Modificacion modificacion = new Modificacion
            {
                Tabla = TABLA_MODIFICACIONES,
                Anterior = $"Nota de recogida {nota.Pedido}: sin abono" + (nota.PedidoAbono.HasValue ? $" (el {nota.PedidoAbono} ya no existe)" : string.Empty),
                Nuevo = $"Pedido de abono {numero} (serie {cabecera.Serie?.Trim()}) para la nota de recogida {nota.Pedido}" +
                    (nota.FacturaOriginal != null ? $", factura original {nota.FacturaOriginal}" : ", sin factura original (LIFO)") +
                    $" (NestoAPI#760, usuario {usuario})",
                Usuario = Recortar(usuario, 50)
            };
            bool guardado;
            try
            {
                guardado = await repositorio.GuardarAbono(cabecera, modificacion, nota.Pedido, nota.PedidoAbono, usuario).ConfigureAwait(false);
            }
            catch
            {
                DevolverNumero(numero);
                throw;
            }
            if (!guardado)
            {
                DevolverNumero(numero);
                // Otro bulto de la misma recogida se ha leído a la vez y ha ganado: se devuelve el suyo.
                NotaRecogidaLeida releida = await repositorio.LeerNota(nota.Empresa, nota.Pedido).ConfigureAwait(false);
                AbonoRecogidaDTO ganador = releida == null ? null : await AbonoExistente(releida).ConfigureAwait(false);
                if (ganador != null)
                {
                    return ResultadoAbonoRecogida<AbonoRecogidaDTO>.Ok(ganador);
                }
                return ResultadoAbonoRecogida<AbonoRecogidaDTO>.Conflicto(
                    $"No se ha podido crear el abono de la nota {nota.Pedido} porque otro usuario la estaba procesando a la vez. Vuelva a leer la etiqueta.",
                    null, null);
            }
            return ResultadoAbonoRecogida<AbonoRecogidaDTO>.Ok(Respuesta(nota, numero, nota.Cliente, nota.Contacto, cabecera.Serie?.Trim(), false), creado: true);
        }

        /// <summary>El número reservado no se ha usado: se devuelve al contador para no dejar un hueco (si sigue siendo el último).</summary>
        private void DevolverNumero(int numero)
        {
            try
            {
                repositorio.DevolverNumeroPedido(numero);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Abono por recogida #760] No se ha podido devolver el número de pedido {numero}: {ex.Message}", ex));
            }
        }

        public async Task<AvisoPedidoDTO> AvisoProductosFueraDeFactura(PedidoVentaDTO pedido)
        {
            try
            {
                if (pedido == null || pedido.Lineas == null || !RegistroSeriesVerifactu.EsSerieRectificativa(pedido.serie?.Trim()))
                {
                    return null;
                }
                List<string> devueltos = pedido.Lineas
                    .Where(l => l != null && l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO && l.Cantidad < 0 && !string.IsNullOrWhiteSpace(l.Producto))
                    .Select(l => l.Producto.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (!devueltos.Any() || !repositorio.ScriptAplicado())
                {
                    return null;
                }
                string empresa = pedido.empresa?.Trim();
                string factura = await repositorio.LeerFacturaOriginalDelAbono(empresa, pedido.numero).ConfigureAwait(false);
                if (factura == null)
                {
                    return null;
                }
                var enFactura = new HashSet<string>(await repositorio.LeerProductosFactura(empresa, factura).ConfigureAwait(false),
                    StringComparer.OrdinalIgnoreCase);
                List<string> fuera = devueltos.Where(p => !enFactura.Contains(p)).ToList();
                if (!fuera.Any())
                {
                    return null;
                }
                return new AvisoPedidoDTO
                {
                    Tipo = TIPO_AVISO_FUERA_DE_FACTURA,
                    Mensaje = MensajeFueraDeFactura(fuera, factura),
                    Datos = new { Factura = factura, Productos = fuera }
                };
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Abono por recogida #760] Pedido {pedido?.numero}: no se ha podido comprobar la factura original: {ex.Message}", ex));
                return null;
            }
        }

        internal static string MensajeFueraDeFactura(IList<string> productos, string factura)
            => productos.Count == 1
                ? $"El producto {productos[0]} no está en la factura {factura}."
                : $"Los productos {string.Join(", ", productos)} no están en la factura {factura}.";

        /// <summary>
        /// La cabecera del abono (pura): cliente y contacto de la nota, serie rectificativa de la de la nota, Periodo NRM, al contado
        /// con vencimiento hoy (como las rectificativas de Copiar factura), forma de pago, IVA, vendedor, ruta, CCC y contacto de cobro
        /// de la ficha, «Abono por recogida: nota …» en Comentarios y el usuario del Identity. TipoRectificativa R1 (devolución) si la
        /// nota trae la factura original. Sin líneas.
        /// </summary>
        internal static CabPedidoVta ConstruirCabecera(NotaRecogidaLeida nota, FichaClienteAbono ficha, int numero, DateTime hoy, string usuario)
            => new CabPedidoVta
            {
                Empresa = nota.Empresa,
                Número = numero,
                Nº_Cliente = nota.Cliente,
                Contacto = nota.Contacto,
                Fecha = hoy.Date,
                Forma_Pago = string.IsNullOrWhiteSpace(ficha.FormaPago) ? FORMA_PAGO_POR_DEFECTO : ficha.FormaPago,
                PlazosPago = PLAZOS_ABONO,
                Primer_Vencimiento = hoy.Date,
                IVA = ficha.Iva,
                Vendedor = ficha.Vendedor,
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                Ruta = ficha.Ruta,
                Serie = SerieAbono(nota.Serie),
                CCC = string.IsNullOrWhiteSpace(ficha.Ccc) ? null : ficha.Ccc,
                Origen = nota.Empresa,
                ContactoCobro = ficha.ContactoCobro,
                // Nada que entregar: ni «mantener junto» (al contado no se puede, #686) ni modo de facturación.
                MantenerJunto = false,
                ServirJunto = true,
                ModoServicio = Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                NotaEntrega = false,
                TipoRectificativa = nota.FacturaOriginal != null ? "R1" : null,
                Comentarios = Comentario(nota),
                Usuario = Recortar(usuario, LONGITUD_USUARIO_PEDIDO)
            };

        internal static string Comentario(NotaRecogidaLeida nota)
            => $"Abono por recogida: nota {nota.Pedido}" + (string.IsNullOrWhiteSpace(nota.TextoNota) ? string.Empty : $" — {nota.TextoNota.Trim()}");

        /// <summary>La serie rectificativa de la de la nota (NV → RV, CV → RC); sin asociada (o sin serie), la de la serie por defecto.</summary>
        internal static string SerieAbono(string serieNota)
        {
            string serie = string.IsNullOrWhiteSpace(serieNota) ? Constantes.Series.SERIE_POR_DEFECTO : serieNota.Trim();
            if (RegistroSeriesVerifactu.EsSerieRectificativa(serie))
            {
                return serie;
            }
            string rectificativa = RegistroSeriesVerifactu.ObtenerSerieRectificativa(serie)
                ?? RegistroSeriesVerifactu.ObtenerSerieRectificativa(Constantes.Series.SERIE_POR_DEFECTO);
            return rectificativa?.Trim();
        }

        private class NotaResuelta
        {
            public NotaRecogidaLeida Nota;
            public string Error;
        }

        /// <summary>«NR929554-1» o «NR929554» → la nota de recogida (empresa 1, decisión 2 de la épica), o el motivo del 404.</summary>
        private async Task<NotaResuelta> Resolver(string codigo)
        {
            string limpio = codigo?.Trim();
            if (!CodigoNotaEntrega.TryLeer(limpio, out string prefijo, out int pedido, out _) || prefijo != CodigoNotaEntrega.PREFIJO_RECOGIDA)
            {
                return new NotaResuelta { Error = $"«{limpio}» no es una etiqueta de recogida (NR…)." };
            }
            NotaRecogidaLeida nota = await repositorio.LeerNota(Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido).ConfigureAwait(false);
            if (nota == null || !nota.NotaEntrega || TiposNotaEntrega.Normalizar(nota.Tipo) != TiposNotaEntrega.RECOGIDA)
            {
                return new NotaResuelta { Error = $"El pedido {pedido} no es una nota de recogida." };
            }
            return new NotaResuelta { Nota = nota };
        }

        /// <summary>El abono ya creado (y que sigue existiendo) de la nota, o null.</summary>
        private async Task<AbonoRecogidaDTO> AbonoExistente(NotaRecogidaLeida nota)
        {
            if (!nota.PedidoAbono.HasValue)
            {
                return null;
            }
            PedidoAbonoLeido abono = await repositorio.LeerPedido(nota.Empresa, nota.PedidoAbono.Value).ConfigureAwait(false);
            return abono == null ? null : Respuesta(nota, nota.PedidoAbono.Value, abono.Cliente, abono.Contacto, abono.Serie, true);
        }

        private static AbonoRecogidaDTO Respuesta(NotaRecogidaLeida nota, int pedido, string cliente, string contacto, string serie, bool yaExistia)
            => new AbonoRecogidaDTO
            {
                Empresa = nota.Empresa,
                Pedido = pedido,
                Cliente = cliente,
                Contacto = contacto,
                Nombre = nota.Nombre,
                Serie = serie,
                PedidoNota = nota.Pedido,
                FacturaOriginal = nota.FacturaOriginal,
                TextoNota = nota.TextoNota,
                YaExistia = yaExistia
            };

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }
}
