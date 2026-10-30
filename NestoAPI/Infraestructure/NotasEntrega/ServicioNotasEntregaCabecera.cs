using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    public enum TipoResultadoNotaEntrega
    {
        Ok,
        NoEncontrado,
        Invalido,
        Conflicto,
        /// <summary>NestoAPI#758: la impresora de etiquetas ha fallado (502).</summary>
        FalloImpresora
    }

    /// <summary>NestoAPI#757: lo que contesta el servicio; el controlador lo traduce a 200/404/400/409.</summary>
    public class ResultadoNotaEntregaCabecera
    {
        public TipoResultadoNotaEntrega Tipo { get; private set; }
        public string Mensaje { get; private set; }
        public NotaEntregaCabeceraDTO Contenido { get; private set; }

        public static ResultadoNotaEntregaCabecera Ok(NotaEntregaCabeceraDTO contenido)
            => new ResultadoNotaEntregaCabecera { Tipo = TipoResultadoNotaEntrega.Ok, Contenido = contenido };
        public static ResultadoNotaEntregaCabecera NoEncontrado(string mensaje)
            => new ResultadoNotaEntregaCabecera { Tipo = TipoResultadoNotaEntrega.NoEncontrado, Mensaje = mensaje };
        public static ResultadoNotaEntregaCabecera Invalido(string mensaje)
            => new ResultadoNotaEntregaCabecera { Tipo = TipoResultadoNotaEntrega.Invalido, Mensaje = mensaje };
        public static ResultadoNotaEntregaCabecera Conflicto(string mensaje)
            => new ResultadoNotaEntregaCabecera { Tipo = TipoResultadoNotaEntrega.Conflicto, Mensaje = mensaje };
    }

    /// <summary>NestoAPI#757: quién puede crear notas de entrega por la API y tocar su cabecera: los mismos que api/RepartoPropio.</summary>
    public static class PermisosNotasEntrega
    {
        public static IReadOnlyList<string> GRUPOS => Controllers.PermisosRepartoPropio.GRUPOS_REPARTO;

        public static bool Puede(IPrincipal usuario) => Controllers.PermisosRepartoPropio.PuedeRepartir(usuario);
    }

    public interface IServicioNotasEntregaCabecera
    {
        /// <summary>GET api/NotasEntrega/{empresa}/{pedido}. NoEncontrado si el pedido no existe o no es nota de entrega.</summary>
        Task<ResultadoNotaEntregaCabecera> Leer(string empresa, int pedido);

        /// <summary>PUT api/NotasEntrega/{empresa}/{pedido}/Cabecera. Deja fila en Modificaciones.</summary>
        Task<ResultadoNotaEntregaCabecera> Actualizar(string empresa, int pedido, ActualizarNotaEntregaCabeceraDTO peticion, string usuario);

        /// <summary>
        /// POST api/PedidosVenta, ANTES de crear nada: quien no está en <see cref="PermisosNotasEntrega"/> sigue creando
        /// pedidos normales (notaEntrega a false, como hasta ahora); para los demás, una nota se deja coherente (Periodo NRM,
        /// sin modo de facturación) y se valida (líneas de texto o de producto, tipo, bultos, factura). Devuelve el motivo
        /// del 400 (de usted) o null.
        /// </summary>
        Task<string> PrepararAlCrear(PedidoVentaDTO pedido, IPrincipal usuario);

        /// <summary>POST api/PedidosVenta, con el pedido YA guardado: la fila de NotasEntregaCabecera. Nunca lanza.</summary>
        Task GuardarAlCrear(PedidoVentaDTO pedido, string usuario);
    }

    /// <summary>
    /// NestoAPI#757 (corte 1 de #755): el tipo (Entrega / Recogida), los bultos y la factura original de una nota de entrega.
    /// Decisiones de Carlos del 30/10/26: las de ENTREGA pasan por picking y packing como cualquier pedido y sus bultos salen
    /// del packing, así que Bultos solo cuenta en las de RECOGIDA (en las de entrega se guarda el valor por defecto y no se
    /// valida). FacturaOriginal solo aplica a Recogida: si llega con Entrega se ignora (se guarda NULL), no da error, para que
    /// el cliente pueda pasar de Recogida a Entrega sin vaciar antes el campo.
    /// </summary>
    public class ServicioNotasEntregaCabecera : IServicioNotasEntregaCabecera
    {
        public const string TABLA_MODIFICACIONES = "NotasEntregaCabecera";
        internal const string MENSAJE_FALTA_SCRIPT = "No se puede guardar el tipo de la nota de entrega: falta lanzar el script " +
            "Scripts/Issue757_NotasEntregaCabecera.sql. Avise a Informática.";

        private readonly IRepositorioNotasEntregaCabecera repositorio;
        private readonly Action<Exception> registrar;

        public ServicioNotasEntregaCabecera(IRepositorioNotasEntregaCabecera repositorio) : this(repositorio, null)
        {
        }

        internal ServicioNotasEntregaCabecera(IRepositorioNotasEntregaCabecera repositorio, Action<Exception> registrar)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        public async Task<ResultadoNotaEntregaCabecera> Leer(string empresa, int pedido)
        {
            empresa = empresa?.Trim();
            PedidoNotaEntrega cabecera = await repositorio.LeerPedido(empresa, pedido).ConfigureAwait(false);
            if (cabecera == null || !cabecera.NotaEntrega)
            {
                return ResultadoNotaEntregaCabecera.NoEncontrado($"El pedido {pedido} no es una nota de entrega.");
            }
            return ResultadoNotaEntregaCabecera.Ok(await Construir(empresa, pedido).ConfigureAwait(false));
        }

        public async Task<ResultadoNotaEntregaCabecera> Actualizar(string empresa, int pedido, ActualizarNotaEntregaCabeceraDTO peticion, string usuario)
        {
            empresa = empresa?.Trim();
            if (peticion == null)
            {
                return ResultadoNotaEntregaCabecera.Invalido("No se ha recibido la cabecera de la nota de entrega.");
            }
            PedidoNotaEntrega cabecera = await repositorio.LeerPedido(empresa, pedido).ConfigureAwait(false);
            if (cabecera == null)
            {
                return ResultadoNotaEntregaCabecera.NoEncontrado($"No existe el pedido {pedido}.");
            }
            if (!cabecera.NotaEntrega)
            {
                return ResultadoNotaEntregaCabecera.Invalido(
                    $"El pedido {pedido} no es una nota de entrega: el tipo y los bultos solo se pueden indicar en una nota de entrega.");
            }
            if (peticion.Tipo == null)
            {
                return ResultadoNotaEntregaCabecera.Invalido(MotivoTipoInvalido(null));
            }
            ValoresCabecera valores = await Validar(empresa, cabecera.Cliente, peticion.Tipo, peticion.Bultos, peticion.FacturaOriginal).ConfigureAwait(false);
            if (valores.Error != null)
            {
                return ResultadoNotaEntregaCabecera.Invalido(valores.Error);
            }
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoNotaEntregaCabecera.Invalido(MENSAJE_FALTA_SCRIPT);
            }
            EnvioNotaEntrega envio = await repositorio.LeerUltimoEnvio(empresa, pedido).ConfigureAwait(false);
            if (envio != null && envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO)
            {
                return ResultadoNotaEntregaCabecera.Conflicto(
                    $"La nota de entrega {pedido} ya está entregada (envío {envio.Numero}): no se puede cambiar su tipo ni sus bultos.");
            }

            FilaNotaEntregaCabecera anterior = await repositorio.Leer(empresa, pedido).ConfigureAwait(false);
            var nueva = new FilaNotaEntregaCabecera
            {
                Empresa = empresa,
                Pedido = pedido,
                Tipo = valores.Tipo,
                Bultos = valores.Bultos,
                FacturaOriginal = valores.FacturaOriginal,
                PedidoAbono = anterior?.PedidoAbono,
                Usuario = usuario
            };
            await repositorio.Guardar(nueva, Modificacion(anterior, nueva, usuario)).ConfigureAwait(false);
            return ResultadoNotaEntregaCabecera.Ok(await Construir(empresa, pedido).ConfigureAwait(false));
        }

        public async Task<string> PrepararAlCrear(PedidoVentaDTO pedido, IPrincipal usuario)
        {
            if (pedido == null)
            {
                return null;
            }
            if (!pedido.notaEntrega || !PermisosNotasEntrega.Puede(usuario))
            {
                // Como hasta ahora: el POST no crea notas para nadie más (y los campos de la nota son de solo lectura).
                pedido.notaEntrega = false;
                LimpiarCamposNota(pedido);
                return null;
            }

            LineaPedidoVentaDTO otraLinea = pedido.Lineas?.FirstOrDefault(l => l != null
                && l.tipoLinea != Constantes.TiposLineaVenta.TEXTO && l.tipoLinea != Constantes.TiposLineaVenta.PRODUCTO);
            if (otraLinea != null)
            {
                string cual = !string.IsNullOrWhiteSpace(otraLinea.Producto) ? otraLinea.Producto.Trim() : otraLinea.texto?.Trim();
                return $"Una nota de entrega solo puede llevar líneas de texto o de producto. Quite la línea {cual} (ni portes ni cuentas contables).";
            }

            ValoresCabecera valores = await Validar(pedido.empresa?.Trim(), pedido.cliente?.Trim(),
                pedido.tipoNotaEntrega ?? TiposNotaEntrega.ENTREGA, pedido.bultosNota, pedido.facturaOriginalNota).ConfigureAwait(false);
            if (valores.Error != null)
            {
                return valores.Error;
            }
            if (valores.Tipo == TiposNotaEntrega.RECOGIDA && !repositorio.ScriptAplicado())
            {
                return MENSAJE_FALTA_SCRIPT;
            }

            // Las reglas de una nota al crearla: Periodo NRM, sin modo de facturación (ni «servir lo pendiente», que va por
            // su endpoint, #688) y sin «mantener junto». Los portes ya no los pone GestorPortes (mira notaEntrega).
            pedido.periodoFacturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL;
            pedido.modoFacturacion = null;
            pedido.mantenerJunto = false;
            pedido.servirPendienteAlHaberStock = null;
            pedido.tipoNotaEntrega = valores.Tipo;
            pedido.bultosNota = valores.Bultos;
            pedido.facturaOriginalNota = valores.FacturaOriginal;
            return null;
        }

        public async Task GuardarAlCrear(PedidoVentaDTO pedido, string usuario)
        {
            if (pedido == null || !pedido.notaEntrega)
            {
                return;
            }
            try
            {
                if (!repositorio.ScriptAplicado())
                {
                    return; // sin script solo puede ser de Entrega (PrepararAlCrear rechaza la Recogida): sin fila = Entrega
                }
                await repositorio.Guardar(new FilaNotaEntregaCabecera
                {
                    Empresa = pedido.empresa?.Trim(),
                    Pedido = pedido.numero,
                    Tipo = pedido.tipoNotaEntrega ?? TiposNotaEntrega.ENTREGA,
                    Bultos = pedido.bultosNota ?? TiposNotaEntrega.BULTOS_POR_DEFECTO,
                    FacturaOriginal = pedido.facturaOriginalNota,
                    Usuario = usuario
                }, null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Notas de entrega #757] La nota {pedido.numero} se ha creado pero no se ha podido guardar su tipo " +
                    $"({pedido.tipoNotaEntrega}, {pedido.bultosNota} bultos): {ex.Message}", ex));
            }
        }

        /// <summary>
        /// GET del pedido: los campos de solo lectura de la nota (null en un pedido que no es nota; sin fila, Entrega y 1 bulto).
        /// </summary>
        internal static void RellenarCamposPedido(PedidoVentaDTO pedido, FilaNotaEntregaCabecera fila)
        {
            if (pedido == null)
            {
                return;
            }
            if (!pedido.notaEntrega)
            {
                LimpiarCamposNota(pedido);
                return;
            }
            pedido.tipoNotaEntrega = TiposNotaEntrega.Normalizar(fila?.Tipo) ?? TiposNotaEntrega.ENTREGA;
            pedido.bultosNota = fila?.Bultos ?? TiposNotaEntrega.BULTOS_POR_DEFECTO;
            pedido.facturaOriginalNota = string.IsNullOrWhiteSpace(fila?.FacturaOriginal) ? null : fila.FacturaOriginal.Trim();
        }

        internal static string EstadoEnvio(short? estado)
        {
            switch (estado)
            {
                case Constantes.Agencias.ESTADO_TRAMITADO: return "Tramitado";
                case Constantes.Agencias.ESTADO_INCIDENTADO: return "Incidentado";
                case Constantes.Agencias.ESTADO_ENTREGADO: return "Entregado";
                default: return null;
            }
        }

        private static void LimpiarCamposNota(PedidoVentaDTO pedido)
        {
            pedido.tipoNotaEntrega = null;
            pedido.bultosNota = null;
            pedido.facturaOriginalNota = null;
        }

        private async Task<NotaEntregaCabeceraDTO> Construir(string empresa, int pedido)
        {
            FilaNotaEntregaCabecera fila = await repositorio.Leer(empresa, pedido).ConfigureAwait(false);
            EnvioNotaEntrega envio = await repositorio.LeerUltimoEnvio(empresa, pedido).ConfigureAwait(false);
            string tipo = TiposNotaEntrega.Normalizar(fila?.Tipo) ?? TiposNotaEntrega.ENTREGA;
            return new NotaEntregaCabeceraDTO
            {
                Empresa = empresa,
                Pedido = pedido,
                Tipo = tipo,
                Bultos = fila?.Bultos ?? TiposNotaEntrega.BULTOS_POR_DEFECTO,
                FacturaOriginal = string.IsNullOrWhiteSpace(fila?.FacturaOriginal) ? null : fila.FacturaOriginal.Trim(),
                PedidoAbono = fila?.PedidoAbono,
                NumeroNota = await repositorio.LeerNumeroNota(empresa, pedido).ConfigureAwait(false),
                Procesada = await repositorio.TieneLineasProcesadas(empresa, pedido).ConfigureAwait(false),
                EnvioId = envio?.Numero,
                EstadoEnvio = EstadoEnvio(envio?.Estado),
                CodigoDocumento = TiposNotaEntrega.CodigoDocumento(tipo, pedido)
            };
        }

        private class ValoresCabecera
        {
            public string Error;
            public string Tipo;
            public short Bultos;
            public string FacturaOriginal;
        }

        private static string MotivoTipoInvalido(string tipo)
            => $"El tipo de la nota de entrega tiene que ser «{TiposNotaEntrega.ENTREGA}» o «{TiposNotaEntrega.RECOGIDA}»" +
               (string.IsNullOrWhiteSpace(tipo) ? "." : $", no «{tipo.Trim()}».");

        /// <summary>Las reglas comunes al PUT y al POST.</summary>
        private async Task<ValoresCabecera> Validar(string empresa, string cliente, string tipoPedido, short? bultos, string factura)
        {
            string tipo = TiposNotaEntrega.Normalizar(tipoPedido);
            if (tipo == null)
            {
                return new ValoresCabecera { Error = MotivoTipoInvalido(tipoPedido) };
            }
            if (tipo == TiposNotaEntrega.ENTREGA)
            {
                // Los bultos de una entrega salen del packing; la factura original solo es de las recogidas: se ignoran.
                return new ValoresCabecera { Tipo = tipo, Bultos = TiposNotaEntrega.BULTOS_POR_DEFECTO };
            }

            short bultosRecogida = bultos ?? TiposNotaEntrega.BULTOS_POR_DEFECTO;
            if (bultosRecogida < 1)
            {
                return new ValoresCabecera { Error = "Una nota de recogida tiene que llevar al menos un bulto." };
            }
            if (bultosRecogida > TiposNotaEntrega.BULTOS_MAXIMO)
            {
                return new ValoresCabecera { Error = $"Una nota de recogida no puede llevar más de {TiposNotaEntrega.BULTOS_MAXIMO} bultos." };
            }

            string facturaOriginal = string.IsNullOrWhiteSpace(factura) ? null : factura.Trim().ToUpperInvariant();
            if (facturaOriginal != null)
            {
                string clienteFactura = facturaOriginal.Length > 10 ? null
                    : await repositorio.LeerClienteFactura(empresa, facturaOriginal).ConfigureAwait(false);
                if (clienteFactura == null)
                {
                    return new ValoresCabecera { Error = $"La factura {facturaOriginal} no existe. Revise el número o déjelo en blanco." };
                }
                if (!string.Equals(clienteFactura.Trim(), cliente?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return new ValoresCabecera
                    {
                        Error = $"La factura {facturaOriginal} es del cliente {clienteFactura.Trim()}, no del cliente de la nota ({cliente?.Trim()})."
                    };
                }
            }
            return new ValoresCabecera { Tipo = tipo, Bultos = bultosRecogida, FacturaOriginal = facturaOriginal };
        }

        private static Modificacion Modificacion(FilaNotaEntregaCabecera anterior, FilaNotaEntregaCabecera nueva, string usuario)
            => new Modificacion
            {
                Tabla = TABLA_MODIFICACIONES,
                Anterior = anterior == null
                    ? $"Nota {nueva.Pedido}: sin fila (Entrega, 1 bulto)"
                    : $"Nota {anterior.Pedido}: {Describir(anterior)}",
                Nuevo = $"Nota {nueva.Pedido}: {Describir(nueva)} (usuario {usuario})",
                Usuario = usuario == null || usuario.Length <= 50 ? usuario : usuario.Substring(0, 50)
            };

        private static string Describir(FilaNotaEntregaCabecera fila)
            => $"Tipo={fila.Tipo}, Bultos={fila.Bultos}, FacturaOriginal={fila.FacturaOriginal ?? "(ninguna)"}";
    }
}
