using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>
    /// NestoAPI#631 (stock obsoleto, corte 3 de la épica #628): la decisión de una persona de Compras o Dirección sobre una
    /// propuesta. Nada se aplica solo: la propuesta solo cambia el stock cuando alguien la «Aplica», y queda apuntado quién,
    /// cuándo, el valor anterior y el nuevo (en la fila y en el histórico PropuestasStockDecisiones).
    /// <para>Qué escribe «Aplicar», según la acción propuesta:</para>
    /// <list type="bullet">
    /// <item>«Ajustar máx/mín»: máximo y mínimo del almacén de la propuesta en ControlesStock (los propuestos o los que diga la
    /// persona, con motivo). Si no hay control de stock, lo crea.</item>
    /// <item>«Pasar a estado 1»: pone a 0 el máximo y el mínimo de TODOS los almacenes y el producto en estado 1. Es lo que exige
    /// el trigger trgProductosUpd: no deja sacar un producto del estado 0 mientras tenga algún máximo.</item>
    /// <item>«Liquidar» (Outlet o Ganavisiones): decisión de Carlos (15/10/26), pone a 0 el máximo y el mínimo del almacén de
    /// la propuesta (lo que propone el motor; con mínimo mayor que el máximo se seguiría reponiendo). NestoAPI#628 (Carlos,
    /// 16/10/26): con el canal Outlet (o sin canal) además pasa el producto a estado 4, y el cuadre nocturno del Outlet (#643,
    /// que solo mete productos en estado 4) lo mete si tiene ficha en la tienda; por eso quedan a 0 los máximos y mínimos de
    /// TODOS los almacenes, como en «Pasar a estado 1» (trgProductosUpd no deja sacar un producto del estado 0 con algún
    /// máximo). Con Ganavisiones no cambia el estado: solo el máximo y el mínimo del almacén, y el paso es manual. Los propios
    /// (fabricación propia) nunca se liquidan: 400.</item>
    /// <item>«Parar compras»: solo se apunta; la acción es manual.</item>
    /// </list>
    /// <para>«Silenciar» (con un motivo de <see cref="MotivosSilencioVigentes"/> y fecha de caducidad): las propuestas del producto de los meses
    /// siguientes salen como «Silenciada» y no como «Pendiente» hasta que caduca. «Posponer»: no se hace nada y el mes que viene
    /// vuelve a salir.</para>
    /// <para>NestoAPI#655: «Aplicar» en las acciones de los propios («Ajustar el siguiente lote», «Potenciar ventas», «Caduca antes
    /// de venderse») escribe el máximo y el mínimo de la fórmula P, como un «Ajustar máx/mín». «Potenciar» (objetivo en uds/mes y
    /// fecha de revisión, por defecto 6 meses; vale para cualquier propuesta con acción, aunque está pensada para los propios): no
    /// escribe nada en el stock; hasta la fecha, las filas del producto salen «En potenciación» con la venta media frente al
    /// objetivo, y pasada la fecha vuelven a su estado normal con la nota «potenciación cumplida» o «sin cumplir (objetivo X, real Y)».</para>
    /// </summary>
    public class ServicioDecisionesPropuestasStock
    {
        internal const int LONGITUD_MOTIVO = 500;
        internal const int LONGITUD_USUARIO = 50;
        /// <summary>Un silencio no puede durar más: si no, el producto desaparece para siempre de la lista.</summary>
        internal const int MESES_MAXIMOS_SILENCIO = 24;
        internal const short ESTADO_DISPONIBLE = 0;
        internal const short ESTADO_SOBRE_PEDIDO = 1;
        /// <summary>NestoAPI#628: «Liquidar» por el Outlet deja el producto en estado 4 (a extinguir).</summary>
        internal const short ESTADO_A_EXTINGUIR = NestoAPI.Models.Constantes.Productos.ESTADO_A_EXTINGUIR;
        internal const string MENSAJE_PROPIOS_NO_SE_LIQUIDAN = "Los productos propios (fabricación propia) nunca se liquidan: no se puede aplicar «Liquidar». " +
            "Recházela con el motivo o decida otra cosa (ajustar el siguiente lote o potenciar las ventas).";
        /// <summary>NestoAPI#655: la fecha de revisión de una potenciación, si no se dice (decisión de Carlos: 6 meses).</summary>
        internal const int MESES_POTENCIACION_POR_DEFECTO = 6;
        /// <summary>NestoAPI#655: como el silencio, una potenciación no puede esconder el producto para siempre.</summary>
        internal const int MESES_MAXIMOS_POTENCIACION = 24;
        internal const string MENSAJE_SIN_POTENCIACION = "Falta el script de «Potenciar ventas» (Scripts/Issue655_PotenciarVentas.sql): de momento solo se puede aplicar, rechazar, posponer o silenciar.";

        private readonly IRepositorioDecisionesPropuestasStock repositorio;
        private readonly Func<DateTime> ahora;

        public ServicioDecisionesPropuestasStock(IRepositorioDecisionesPropuestasStock repositorio, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        private static DecisionPropuestaStockException NoValida(string mensaje) => new DecisionPropuestaStockException(HttpStatusCode.BadRequest, mensaje);
        private static DecisionPropuestaStockException Conflicto(string mensaje) => new DecisionPropuestaStockException(HttpStatusCode.Conflict, mensaje);

        public ResultadoDecisionPropuestaStock Decidir(int id, DecisionPropuestaStockDTO peticion, string usuario)
        {
            if (peticion == null)
            {
                throw NoValida("Falta la decisión.");
            }
            string decision = DecisionesPropuesta.Normalizar(peticion.Decision);
            if (decision == null)
            {
                throw NoValida($"La decisión tiene que ser {string.Join(", ", DecisionesPropuesta.TODAS)}.");
            }
            if (decision == DecisionesPropuesta.POTENCIAR && !repositorio.ExistePotenciacion())
            {
                // Sin el script, la API va como antes: solo esta decisión no se puede tomar (404, como el resto de «falta el script»).
                throw new DecisionPropuestaStockException(HttpStatusCode.NotFound, MENSAJE_SIN_POTENCIACION);
            }
            string motivo = string.IsNullOrWhiteSpace(peticion.Motivo) ? null : peticion.Motivo.Trim();
            if (motivo != null && motivo.Length > LONGITUD_MOTIVO)
            {
                throw NoValida($"El motivo no puede pasar de {LONGITUD_MOTIVO} caracteres.");
            }

            PropuestaStockConDecision propuesta = repositorio.LeerPropuesta(id)
                ?? throw new DecisionPropuestaStockException(HttpStatusCode.NotFound, $"No existe la propuesta {id}.");
            ComprobarQueSePuedeDecidir(propuesta);

            bool esAjuste = AccionesPropuesta.EsAjusteDeMaximos(propuesta.Accion);
            if ((peticion.StockMaximo.HasValue || peticion.StockMinimo.HasValue) && !(decision == DecisionesPropuesta.APLICAR && esAjuste))
            {
                throw NoValida("El máximo y el mínimo solo se indican al aplicar una propuesta de «Ajustar máx/mín» (o una de los propios).");
            }
            if (decision != DecisionesPropuesta.POTENCIAR && (peticion.ObjetivoVentaMensual.HasValue || peticion.PotenciarHasta.HasValue))
            {
                throw NoValida("El objetivo de venta y la fecha de revisión solo se indican al potenciar.");
            }
            if (!string.IsNullOrWhiteSpace(peticion.CanalLiquidacion) && !(decision == DecisionesPropuesta.APLICAR && propuesta.Accion == AccionesPropuesta.LIQUIDAR))
            {
                throw NoValida("El canal de liquidación solo se indica al aplicar una propuesta de «Liquidar».");
            }
            if (decision != DecisionesPropuesta.SILENCIAR && (!string.IsNullOrWhiteSpace(peticion.MotivoSilencio) || peticion.SilenciarHasta.HasValue))
            {
                throw NoValida("El motivo del silencio y la fecha de caducidad solo se indican al silenciar.");
            }

            DateTime momento = ahora();
            RegistroDecisionPropuesta registro = new RegistroDecisionPropuesta
            {
                PropuestaId = propuesta.Id,
                Producto = propuesta.Producto?.Trim(),
                Almacen = propuesta.Almacen?.Trim(),
                Mes = propuesta.Mes,
                DecisionAnterior = propuesta.Decision,
                DecisionFechaAnterior = propuesta.DecisionFecha,
                Decision = decision,
                Usuario = Cortar(string.IsNullOrWhiteSpace(usuario) ? "(desconocido)" : usuario.Trim(), LONGITUD_USUARIO),
                Fecha = momento,
                Motivo = motivo
            };
            List<string> cambios = new List<string>();

            switch (decision)
            {
                case DecisionesPropuesta.RECHAZAR:
                    if (motivo == null)
                    {
                        throw NoValida("Para rechazar una propuesta indique el motivo: sirve para ajustar las fórmulas.");
                    }
                    break;
                case DecisionesPropuesta.SILENCIAR:
                    PrepararSilencio(peticion, motivo, momento, registro);
                    break;
                case DecisionesPropuesta.APLICAR:
                    PrepararAplicacion(propuesta, peticion, motivo, registro, cambios);
                    break;
                case DecisionesPropuesta.POTENCIAR:
                    PrepararPotenciacion(peticion, momento, registro);
                    break;
            }

            registro.Cambios = cambios.Count == 0 ? null : string.Join("; ", cambios);
            repositorio.GuardarDecision(registro);

            return new ResultadoDecisionPropuestaStock
            {
                Id = propuesta.Id,
                Producto = registro.Producto,
                Decision = decision,
                Estado = EstadoTras(decision),
                Cambios = cambios,
                Mensaje = Mensaje(propuesta, registro, cambios)
            };
        }

        private void ComprobarQueSePuedeDecidir(PropuestaStockConDecision propuesta)
        {
            CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
            if (propuesta.Decision == DecisionesPropuesta.APLICAR || propuesta.Estado == EstadosPropuesta.APLICADA)
            {
                throw Conflicto($"La propuesta del producto {propuesta.Producto?.Trim()} ya está aplicada " +
                    $"({propuesta.DecisionUsuario?.Trim()}, {propuesta.DecisionFecha:dd/MM/yyyy HH:mm}). Para cambiar algo, hágalo en la ficha del producto.");
            }
            if (propuesta.MesMasReciente.HasValue && propuesta.MesMasReciente.Value > propuesta.Mes)
            {
                throw Conflicto($"Esta propuesta es de {propuesta.Mes.ToString("MMMM yyyy", es)} y ya hay una más reciente del producto " +
                    $"{propuesta.Producto?.Trim()} ({propuesta.MesMasReciente.Value.ToString("MMMM yyyy", es)}): decida sobre la última.");
            }
            if (propuesta.Accion == AccionesPropuesta.MANTENER || propuesta.Accion == AccionesPropuesta.NADA || propuesta.Estado == EstadosPropuesta.SIN_ACCION)
            {
                throw NoValida($"La propuesta del producto {propuesta.Producto?.Trim()} es «{propuesta.Accion}»: no hay nada que decidir.");
            }
        }

        /// <summary>Los motivos de silencio que valen ahora (parámetro <see cref="MotivosSilencio.CLAVE"/>; sin él, Nuevo, Estacional y Aparato).</summary>
        public List<string> MotivosSilencioVigentes() => MotivosSilencio.Lista(repositorio.LeerParametroMotivosSilencio());

        private void PrepararSilencio(DecisionPropuestaStockDTO peticion, string motivo, DateTime momento, RegistroDecisionPropuesta registro)
        {
            List<string> motivos = MotivosSilencioVigentes();
            string motivoSilencio = MotivosSilencio.Normalizar(peticion.MotivoSilencio, motivos);
            if (motivoSilencio == null)
            {
                throw NoValida($"Para silenciar indique el motivo del silencio: {string.Join(", ", motivos)}.");
            }
            if (!peticion.SilenciarHasta.HasValue)
            {
                throw NoValida("Para silenciar indique hasta cuándo (la fecha de caducidad).");
            }
            DateTime hasta = peticion.SilenciarHasta.Value.Date;
            if (hasta <= momento.Date)
            {
                throw NoValida("La fecha de caducidad del silencio tiene que ser posterior a hoy.");
            }
            if (hasta > momento.Date.AddMonths(MESES_MAXIMOS_SILENCIO))
            {
                throw NoValida($"Un silencio no puede durar más de {MESES_MAXIMOS_SILENCIO} meses.");
            }
            registro.MotivoSilencio = motivoSilencio;
            registro.SilenciadaHasta = hasta;
        }

        /// <summary>
        /// NestoAPI#655: «Potenciar ventas»: objetivo (uds/mes, obligatorio) y fecha de revisión (por defecto, dentro de
        /// <see cref="MESES_POTENCIACION_POR_DEFECTO"/> meses). No escribe nada en el stock: hasta la fecha, las propuestas del producto
        /// salen «En potenciación» y no como pendientes.
        /// </summary>
        private static void PrepararPotenciacion(DecisionPropuestaStockDTO peticion, DateTime momento, RegistroDecisionPropuesta registro)
        {
            if (!peticion.ObjetivoVentaMensual.HasValue || peticion.ObjetivoVentaMensual.Value <= 0)
            {
                throw NoValida("Para potenciar las ventas indique el objetivo de venta (unidades al mes, más de 0).");
            }
            DateTime hasta = (peticion.PotenciarHasta ?? momento.Date.AddMonths(MESES_POTENCIACION_POR_DEFECTO)).Date;
            if (hasta <= momento.Date)
            {
                throw NoValida("La fecha de revisión de la potenciación tiene que ser posterior a hoy.");
            }
            if (hasta > momento.Date.AddMonths(MESES_MAXIMOS_POTENCIACION))
            {
                throw NoValida($"Una potenciación no puede durar más de {MESES_MAXIMOS_POTENCIACION} meses.");
            }
            registro.ObjetivoVentaMensual = peticion.ObjetivoVentaMensual.Value;
            registro.PotenciarHasta = hasta;
        }

        private void PrepararAplicacion(PropuestaStockConDecision propuesta, DecisionPropuestaStockDTO peticion, string motivo,
            RegistroDecisionPropuesta registro, List<string> cambios)
        {
            string producto = propuesta.Producto?.Trim();
            string almacen = propuesta.Almacen?.Trim();
            string canalLiquidacion = null;
            bool liquidarAEstado4 = false;
            if (propuesta.Accion == AccionesPropuesta.LIQUIDAR)
            {
                // NestoAPI#655/#628: el motor nunca propone liquidar un propio; si llega (fila antigua, cliente que se equivoca), no.
                if (propuesta.FabricacionPropia)
                {
                    throw NoValida(MENSAJE_PROPIOS_NO_SE_LIQUIDAN);
                }
                if (!string.IsNullOrWhiteSpace(peticion.CanalLiquidacion))
                {
                    canalLiquidacion = CanalesLiquidacion.Normalizar(peticion.CanalLiquidacion)
                        ?? throw NoValida($"El canal de liquidación tiene que ser {string.Join(" o ", CanalesLiquidacion.TODOS)}.");
                }
                liquidarAEstado4 = LiquidarPasaAEstado4(canalLiquidacion);
            }
            bool escribe = AccionesPropuesta.EsAjusteDeMaximos(propuesta.Accion) || propuesta.Accion == AccionesPropuesta.ESTADO_1 || liquidarAEstado4;
            short? estadoActual = null;
            if (escribe)
            {
                estadoActual = repositorio.LeerEstadoProducto(producto);
                if (!estadoActual.HasValue)
                {
                    throw new DecisionPropuestaStockException(HttpStatusCode.NotFound, $"No existe el producto {producto}.");
                }
                // Si alguien ya lo ha pasado a estado 4 a mano, liquidar por el Outlet sigue valiendo (solo quita los máximos).
                bool yaEnEstado4 = liquidarAEstado4 && estadoActual.Value == ESTADO_A_EXTINGUIR;
                if (!yaEnEstado4 && propuesta.EstadoProducto.HasValue && estadoActual.Value != propuesta.EstadoProducto.Value)
                {
                    throw Conflicto($"El producto {producto} ha cambiado de estado desde que se calculó la propuesta (era {propuesta.EstadoProducto}, " +
                        $"ahora {estadoActual}): esta propuesta ya no vale. Espere a la del mes que viene o cámbielo en la ficha.");
                }
            }

            switch (propuesta.Accion)
            {
                case AccionesPropuesta.AJUSTAR:
                // NestoAPI#655: las tres de los propios llevan el máximo y el mínimo de la fórmula P: aplicarlas es escribirlos.
                case AccionesPropuesta.AJUSTAR_LOTE:
                case AccionesPropuesta.POTENCIAR:
                case AccionesPropuesta.CADUCA:
                    {
                        int? maximo = peticion.StockMaximo ?? propuesta.StockMaximoPropuesto;
                        int? minimo = peticion.StockMinimo ?? propuesta.StockMinimoPropuesto;
                        if (!maximo.HasValue || !minimo.HasValue)
                        {
                            throw NoValida("La propuesta no tiene máximo y mínimo: indíquelos usted.");
                        }
                        if (minimo.Value < 0 || maximo.Value < 0 || maximo.Value > short.MaxValue)
                        {
                            throw NoValida($"El máximo y el mínimo tienen que estar entre 0 y {short.MaxValue}.");
                        }
                        if (minimo.Value > maximo.Value)
                        {
                            throw NoValida("El mínimo no puede ser mayor que el máximo.");
                        }
                        bool conCambios = maximo != propuesta.StockMaximoPropuesto || minimo != propuesta.StockMinimoPropuesto;
                        if (conCambios && motivo == null)
                        {
                            throw NoValida($"Si no aplica el máximo y el mínimo propuestos ({propuesta.StockMaximoPropuesto}/{propuesta.StockMinimoPropuesto}), indique el motivo.");
                        }
                        ControlStockActual actual = repositorio.LeerControlesStock(producto)
                            .FirstOrDefault(c => string.Equals(c.Almacen?.Trim(), almacen, StringComparison.OrdinalIgnoreCase));
                        registro.StockMaximoAnterior = actual?.StockMaximo;
                        registro.StockMinimoAnterior = actual?.StockMinimo;
                        registro.StockMaximoAplicado = maximo;
                        registro.StockMinimoAplicado = minimo;
                        if (actual == null || actual.StockMaximo != maximo.Value || actual.StockMinimo != minimo.Value)
                        {
                            CambioControlStock cambio = new CambioControlStock
                            {
                                Almacen = almacen,
                                Existia = actual != null,
                                MaximoAnterior = actual?.StockMaximo,
                                MinimoAnterior = actual?.StockMinimo,
                                MaximoNuevo = maximo.Value,
                                MinimoNuevo = minimo.Value,
                                Multiplos = Math.Max(1, propuesta.Multiplos)
                            };
                            registro.CambiosControlStock.Add(cambio);
                            cambios.Add(cambio.Texto());
                        }
                        break;
                    }
                case AccionesPropuesta.ESTADO_1:
                    {
                        // A propósito, TODOS los almacenes, tiendas incluidas (Carlos, 15/10/26): «Estado 0 es el único estado del
                        // que queremos tener stock, el resto son sobre pedido». Además lo exige trgProductosUpd.
                        if (estadoActual.Value == ESTADO_SOBRE_PEDIDO)
                        {
                            throw Conflicto($"El producto {producto} ya está en estado {ESTADO_SOBRE_PEDIDO}.");
                        }
                        List<ControlStockActual> controles = repositorio.LeerControlesStock(producto);
                        ControlStockActual delAlmacen = controles.FirstOrDefault(c => string.Equals(c.Almacen?.Trim(), almacen, StringComparison.OrdinalIgnoreCase));
                        registro.StockMaximoAnterior = delAlmacen?.StockMaximo;
                        registro.StockMinimoAnterior = delAlmacen?.StockMinimo;
                        registro.StockMaximoAplicado = delAlmacen == null ? (int?)null : 0;
                        registro.StockMinimoAplicado = delAlmacen == null ? (int?)null : 0;
                        foreach (ControlStockActual c in controles.Where(c => c.StockMaximo != 0 || c.StockMinimo != 0).OrderBy(c => c.Almacen))
                        {
                            CambioControlStock cambio = new CambioControlStock
                            {
                                Almacen = c.Almacen?.Trim(),
                                Existia = true,
                                MaximoAnterior = c.StockMaximo,
                                MinimoAnterior = c.StockMinimo,
                                MaximoNuevo = 0,
                                MinimoNuevo = 0
                            };
                            registro.CambiosControlStock.Add(cambio);
                            cambios.Add(cambio.Texto());
                        }
                        registro.EstadoAnterior = estadoActual;
                        registro.EstadoAplicado = ESTADO_SOBRE_PEDIDO;
                        cambios.Add($"Estado {estadoActual} → {ESTADO_SOBRE_PEDIDO}");
                        break;
                    }
                case AccionesPropuesta.LIQUIDAR:
                    {
                        registro.CanalLiquidacion = canalLiquidacion;
                        List<ControlStockActual> controles = repositorio.LeerControlesStock(producto);
                        ControlStockActual actual = controles
                            .FirstOrDefault(c => string.Equals(c.Almacen?.Trim(), almacen, StringComparison.OrdinalIgnoreCase));
                        if (liquidarAEstado4)
                        {
                            // NestoAPI#628 (Outlet o sin canal): estado 4. TODOS los almacenes a 0, como «Pasar a estado 1»: lo exige
                            // trgProductosUpd al salir del estado 0, y en estado 4 no se repone en ningún sitio. Sin control no se crea.
                            registro.StockMaximoAnterior = actual?.StockMaximo;
                            registro.StockMinimoAnterior = actual?.StockMinimo;
                            registro.StockMaximoAplicado = actual == null ? (int?)null : 0;
                            registro.StockMinimoAplicado = actual == null ? (int?)null : 0;
                            foreach (ControlStockActual c in controles
                                .Where(c => c.StockMaximo != 0 || c.StockMinimo != 0)
                                .OrderBy(c => string.Equals(c.Almacen?.Trim(), almacen, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                .ThenBy(c => c.Almacen))
                            {
                                CambioControlStock cambio = new CambioControlStock
                                {
                                    Almacen = c.Almacen?.Trim(),
                                    Existia = true,
                                    MaximoAnterior = c.StockMaximo,
                                    MinimoAnterior = c.StockMinimo,
                                    MaximoNuevo = 0,
                                    MinimoNuevo = 0
                                };
                                registro.CambiosControlStock.Add(cambio);
                                cambios.Add(cambio.Texto());
                            }
                            if (estadoActual.Value != ESTADO_A_EXTINGUIR)
                            {
                                // Por el mismo camino que «Pasar a estado 1» (SQL_ESTADO_NUEVO en el repositorio): el usuario de la
                                // decisión en Productos.Usuario, y trgProductosUpd apunta la auditoría y encola en Nesto_sync.
                                registro.EstadoAnterior = estadoActual;
                                registro.EstadoAplicado = ESTADO_A_EXTINGUIR;
                                cambios.Add($"Estado {estadoActual} → {ESTADO_A_EXTINGUIR}");
                            }
                            break;
                        }
                        // Ganavisiones: solo el almacén de la propuesta. Sin control de stock no hay máximo que quitar: no se crea uno a 0.
                        if (actual != null)
                        {
                            registro.StockMaximoAnterior = actual.StockMaximo;
                            registro.StockMinimoAnterior = actual.StockMinimo;
                            registro.StockMaximoAplicado = 0;
                            registro.StockMinimoAplicado = 0;
                            if (actual.StockMaximo != 0 || actual.StockMinimo != 0)
                            {
                                CambioControlStock cambio = new CambioControlStock
                                {
                                    Almacen = almacen,
                                    Existia = true,
                                    MaximoAnterior = actual.StockMaximo,
                                    MinimoAnterior = actual.StockMinimo,
                                    MaximoNuevo = 0,
                                    MinimoNuevo = 0
                                };
                                registro.CambiosControlStock.Add(cambio);
                                cambios.Add(cambio.Texto());
                            }
                        }
                        break;
                    }
                case AccionesPropuesta.PARAR_COMPRAS:
                    break;
                default:
                    throw NoValida($"No se sabe aplicar una propuesta «{propuesta.Accion}».");
            }
        }

        /// <summary>
        /// NestoAPI#628: «Liquidar» pasa a estado 4 (y así al Outlet en la pasada nocturna) salvo que el canal elegido sea
        /// Ganavisiones. Sin canal cuenta como Outlet.
        /// </summary>
        internal static bool LiquidarPasaAEstado4(string canalNormalizado) => canalNormalizado != CanalesLiquidacion.GANAVISIONES;

        internal static string EstadoTras(string decision)
        {
            switch (decision)
            {
                case DecisionesPropuesta.APLICAR: return EstadosPropuesta.APLICADA;
                case DecisionesPropuesta.RECHAZAR: return EstadosPropuesta.RECHAZADA;
                case DecisionesPropuesta.SILENCIAR: return EstadosPropuesta.SILENCIADA;
                case DecisionesPropuesta.POTENCIAR: return EstadosPropuesta.EN_POTENCIACION;
                default: return EstadosPropuesta.POSPUESTA;
            }
        }

        private static string Mensaje(PropuestaStockConDecision propuesta, RegistroDecisionPropuesta registro, List<string> cambios)
        {
            string producto = registro.Producto;
            switch (registro.Decision)
            {
                case DecisionesPropuesta.APLICAR:
                    if (propuesta.Accion == AccionesPropuesta.LIQUIDAR)
                    {
                        string escrito = cambios.Count > 0
                            ? string.Join("; ", cambios)
                            : registro.StockMaximoAplicado.HasValue
                                ? $"el máximo y el mínimo de {registro.Almacen} ya estaban a 0"
                                : $"no hay control de stock en {registro.Almacen}, no se ha cambiado nada";
                        if (!LiquidarPasaAEstado4(registro.CanalLiquidacion))
                        {
                            return $"Propuesta del producto {producto} aplicada: {escrito}. El estado no cambia: el paso a Ganavisiones lo hace usted a mano.";
                        }
                        string outlet = registro.EstadoAplicado == ESTADO_A_EXTINGUIR
                            ? $"El producto pasa a estado {ESTADO_A_EXTINGUIR}"
                            : $"El producto ya estaba en estado {ESTADO_A_EXTINGUIR}";
                        return $"Propuesta del producto {producto} aplicada: {escrito}. {outlet}: si tiene ficha en la tienda, entra en el Outlet en la pasada nocturna siguiente.";
                    }
                    if (cambios.Count > 0)
                    {
                        return $"Propuesta del producto {producto} aplicada: {string.Join("; ", cambios)}.";
                    }
                    if (AccionesPropuesta.EsAjusteDeMaximos(propuesta.Accion))
                    {
                        return $"Propuesta del producto {producto} aplicada: el máximo y el mínimo ya eran esos, no se ha cambiado nada.";
                    }
                    string canal = registro.CanalLiquidacion == null ? "" : $" ({registro.CanalLiquidacion})";
                    return $"Propuesta del producto {producto} aceptada: «{propuesta.Accion}»{canal}. No se cambia nada automáticamente; la acción la hace usted.";
                case DecisionesPropuesta.RECHAZAR:
                    return $"Propuesta del producto {producto} rechazada.";
                case DecisionesPropuesta.SILENCIAR:
                    return $"Producto {producto} silenciado hasta el {registro.SilenciadaHasta:dd/MM/yyyy} ({registro.MotivoSilencio}).";
                case DecisionesPropuesta.POTENCIAR:
                    return $"Producto {producto} en potenciación hasta el {registro.PotenciarHasta:dd/MM/yyyy}: objetivo {registro.ObjetivoVentaMensual} uds/mes " +
                        $"(ahora vende {MotorTendencias.Num(propuesta.VentaMedia6Meses, 1)} de media). Si no se cumple, volverá a salir con la nota «potenciación sin cumplir».";
                default:
                    return $"Propuesta del producto {producto} pospuesta: volverá a salir el mes que viene.";
            }
        }

        private static string Cortar(string texto, int longitud) => texto.Length <= longitud ? texto : texto.Substring(0, longitud);
    }
}
