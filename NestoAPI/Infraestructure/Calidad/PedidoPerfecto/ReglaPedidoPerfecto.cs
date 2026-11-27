using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NestoAPI.Infraestructure.Calidad.PedidoPerfecto
{
    // NestoAPI#914 (corte 1, Carlos 26/11/26): «Pedido perfecto», solo lectura y en sombra. Definición de Carlos: «lo meto, tenemos
    // stock de todo, lo servimos al día siguiente, se cobra al contado y no hay ningún problema»; todo lo que se salga es un motivo.
    // Las definiciones exactas (la referencia de toda la épica #914) están en docs/claude/calidad-kpi.md.

    /// <summary>Texto (de usted) y si es un error o solo informativo (<see cref="EsError"/> = false: no se sale por culpa de nadie).</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MotivoPedidoPerfectoAttribute : Attribute
    {
        public MotivoPedidoPerfectoAttribute(string texto, bool esError = true)
        {
            Texto = texto;
            EsError = esError;
        }

        public string Texto { get; }
        public bool EsError { get; }
    }

    /// <summary>Por qué un pedido no fue perfecto. Puede tener varios.</summary>
    public enum MotivoPedidoPerfecto
    {
        [MotivoPedidoPerfecto("No se sirvió de una vez: salió en más de un albarán o quedó algo pendiente después de servir el resto.")]
        NoServidoDeUnaVez = 1,

        [MotivoPedidoPerfecto("No se ha servido nada pasado el plazo.")]
        SinServir = 2,

        [MotivoPedidoPerfecto("No salió al día siguiente: del día del pedido (o de la fecha de entrega que pidió el cliente) al primer albarán pasó más de un día laborable.")]
        NoSalioAlDiaSiguiente = 3,

        [MotivoPedidoPerfecto("Al entrar no había stock de todo en Algete.")]
        FaltabaStock = 4,

        [MotivoPedidoPerfecto("No se cobró al contado (plazos de pago que no son contado ni prepago).", esError: false)]
        NoCobradoAlContado = 5,

        // 6 era TieneRectificativaOAbono: se partió en EntregaIncompleta, FacturaRectificadaEntera, FacturaRectificadaEnParte y
        // TieneCopia (Carlos 27/11/26). El número no se reutiliza.

        [MotivoPedidoPerfecto("Algún efecto de sus facturas se devolvió (impagado).")]
        Impagado = 7,

        [MotivoPedidoPerfecto("Incidencia de la agencia: más de un envío, reenvío, devuelto, incidentado o cerrado a mano.")]
        IncidenciaDeAgenciaOReenvio = 8,

        [MotivoPedidoPerfecto("Se tocó después de sacarle el picking (se le quitó el picking o se le añadieron líneas en preparación).")]
        TocadoTrasElPicking = 9,

        [MotivoPedidoPerfecto("Envío de nuestra ruta metido a mano fuera del día de reparto de su ruta.")]
        EnvioDeRutaPropiaForzado = 10,

        [MotivoPedidoPerfecto("Entrega incompleta de nuestro reparto: faltó algún bulto al entregar.")]
        EntregaIncompletaDelReparto = 11,

        [MotivoPedidoPerfecto("Se pidió cambiar algo cuando ya tenía picking (el modo de entrega, o añadir líneas que acabaron en un pedido nuevo).")]
        PidioCambiarAlgoConPicking = 12,

        [MotivoPedidoPerfecto("Entrega incompleta: se hizo el albarán (y se facturó entero) pero quedó algo pendiente de entregar, que va en una nota de entrega aparte.")]
        EntregaIncompleta = 13,

        [MotivoPedidoPerfecto("Se rectificó entera alguna de sus facturas (se abonaron todas las unidades de producto que se le facturaron).")]
        FacturaRectificadaEntera = 14,

        [MotivoPedidoPerfecto("Se rectificó en parte alguna de sus facturas (se abonaron unidades, pero no todas).")]
        FacturaRectificadaEnParte = 15,

        [MotivoPedidoPerfecto("Otro pedido lo tiene de pedido de origen y no es una nota de entrega de lo pendiente.", esError: false)]
        TieneCopia = 16,

        // Carlos 27/11/26: el pedido que se tuvo que reenviar deja de ser perfecto aunque no tenga rectificativa. La misma regla y la misma
        // lectura que el indicador de Almacén (ReglaErrorEnvioReclamado).
        [MotivoPedidoPerfecto("Error de envío reclamado: después de servirlo se le volvió a mandar algún producto sin coste, o se rectificó y se le mandó otro parecido.")]
        ErrorDeEnvioReclamado = 17
    }

    public static class InfoMotivosPedidoPerfecto
    {
        private static readonly Dictionary<MotivoPedidoPerfecto, MotivoPedidoPerfectoAttribute> atributos =
            Enum.GetValues(typeof(MotivoPedidoPerfecto)).Cast<MotivoPedidoPerfecto>().ToDictionary(m => m, m =>
                typeof(MotivoPedidoPerfecto).GetField(m.ToString()).GetCustomAttribute<MotivoPedidoPerfectoAttribute>());

        public static IReadOnlyList<MotivoPedidoPerfecto> Todos { get; } = atributos.Keys.OrderBy(m => (int)m).ToList();

        public static string Texto(MotivoPedidoPerfecto motivo) => atributos[motivo].Texto;

        public static bool EsError(MotivoPedidoPerfecto motivo) => atributos[motivo].EsError;

        /// <summary>Por qué (o desde cuándo) no se puede evaluar: para MotivosNoEvaluados del resumen.</summary>
        public static string PorQueNoEvaluado(MotivoPedidoPerfecto motivo)
        {
            switch (motivo)
            {
                case MotivoPedidoPerfecto.SinServir:
                    return $"Solo se evalúa pasados {ReglaPedidoPerfecto.DIAS_PLAZO_SIN_SERVIR} días naturales desde el pedido (o desde la fecha de entrega que pidió el cliente); los más recientes, y los que no tienen fecha de entrega (31/12/2099), no cuentan.";
                case MotivoPedidoPerfecto.NoSalioAlDiaSiguiente:
                    return "No se evalúa si no se ha servido nada (eso es «Sin servir»), si el pedido es «Junto con lo pendiente del cliente» (modo 6, espera a propósito) ni si no tiene fecha de entrega (31/12/2099).";
                case MotivoPedidoPerfecto.FaltabaStock:
                    return "Solo hay rastro desde las fotos diarias de las fechas de entrega (dbo.FechasEntregaFotos, desde el 11/11/26). Antes no queda constancia del stock que había al entrar el pedido.";
                case MotivoPedidoPerfecto.NoCobradoAlContado:
                    return "Pedidos sin plazos de pago en la cabecera.";
                case MotivoPedidoPerfecto.TocadoTrasElPicking:
                    return "Solo hay rastro desde el 09/11/26 («Quitar picking» de Nesto en Modificaciones, NestoAPI#590) y desde el primer evento de calidad (dbo.CalidadEventos). Nesto viejo quitaba el picking sin dejar rastro.";
                case MotivoPedidoPerfecto.EnvioDeRutaPropiaForzado:
                    return "No queda rastro de un envío de nuestra ruta metido fuera de su día: no hay un punto único donde se «fuerce». Se evaluará desde el primer evento «EnvioRutaPropiaFueraDeDia» de dbo.CalidadEventos, cuando se conecte.";
                case MotivoPedidoPerfecto.EntregaIncompletaDelReparto:
                    return "Solo hay rastro de las entregas incompletas del reparto propio desde el 19/11/26 (NestoAPI#880); antes no se leían los bultos al entregar.";
                case MotivoPedidoPerfecto.PidioCambiarAlgoConPicking:
                    return "Solo desde el primer evento de calidad (dbo.CalidadEventos): antes la petición solo salía por correo y no quedaba en la BD.";
                case MotivoPedidoPerfecto.ErrorDeEnvioReclamado:
                    return $"Solo los pedidos con algo servido y cuyas líneas servidas tienen albarán de hace al menos los días de «{Reclamaciones.ReglaErrorEnvioReclamado.PARAMETRO_DIAS}» " +
                        $"({Reclamaciones.ReglaErrorEnvioReclamado.DIAS_POR_DEFECTO} si no hay fila en «(defecto)»): antes el cliente aún puede reclamar.";
                default:
                    return "Siempre se evalúa.";
            }
        }
    }

    /// <summary>Lo que se sabe de un pedido, ya leído de la BD (lo rellena <see cref="LectorPedidoPerfectoSql"/>). Sin lógica.</summary>
    public class HechosPedidoPerfecto
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public DateTime Fecha { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Vendedor { get; set; }
        /// <summary>Quien lo metió (PedidosModoServicioOrigen, Crear) o, si no se sabe, el último que tocó la cabecera.</summary>
        public string Usuario { get; set; }
        public bool UsuarioEsDelAlta { get; set; }
        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }
        public byte? ModoServicio { get; set; }
        public bool ServirJunto { get; set; }
        public string Ruta { get; set; }
        /// <summary>Almacén de las líneas de producto (el mayor si hay varios): sus festivos cuentan para los laborables.</summary>
        public string Almacen { get; set; }
        public decimal Importe { get; set; }
        /// <summary>Alguna línea con cantidad negativa (cheque regalo, descuento de portes, devolución suelta…): el pedido entra igual.</summary>
        public bool LlevaLineasNegativas { get; set; }

        /// <summary>Líneas de producto con cantidad positiva (las negativas no cuentan para servido, pendiente ni albaranes).</summary>
        public int LineasProducto { get; set; }
        /// <summary>Líneas de producto en -1 o 1 (pendientes o en curso).</summary>
        public int LineasPendientes { get; set; }
        /// <summary>Líneas de producto en albarán o factura (estado ≥ 2).</summary>
        public int LineasServidas { get; set; }
        public int Albaranes { get; set; }
        public DateTime? PrimerAlbaran { get; set; }
        /// <summary>La menor Fecha Entrega de las líneas de producto.</summary>
        public DateTime? MinFechaEntrega { get; set; }

        // Para el canal
        public int LineasEnTienda { get; set; }
        public int LineasAmazon { get; set; }
        public int LineasTiendaOnline { get; set; }
        public int LineasOtroCanalExterno { get; set; }
        public int LineasApp { get; set; }
        public int LineasCursos { get; set; }

        /// <summary>Otro pedido con PedidoOrigen = este que es la nota de entrega de lo pendiente (#542): <see cref="ReglaPedidoPerfecto.EsNotaEntregaDeLoPendiente"/>.</summary>
        public bool TieneNotaEntregaPendiente { get; set; }
        /// <summary>Otro pedido con PedidoOrigen = este que NO es una nota de entrega de lo pendiente (informativo).</summary>
        public bool TieneCopia { get; set; }
        /// <summary>Sus facturas que son FacturaOriginalNumero de alguna rectificativa, con las unidades de cada una.</summary>
        public List<FacturaRectificadaPedidoPerfecto> FacturasRectificadas { get; set; } = new List<FacturaRectificadaPedidoPerfecto>();
        public bool Impagado { get; set; }

        /// <summary>Envíos del pedido que han salido (Estado ≥ 1).</summary>
        public int EnviosSalidos { get; set; }
        /// <summary>Algún envío incidentado (3), devuelto (4) o cerrado a mano (5) ahora.</summary>
        public bool EnvioConIncidencia { get; set; }
        /// <summary>Algún envío suyo con «Reenviar» (#699); null = sin la tabla.</summary>
        public bool? Reenvio { get; set; }
        /// <summary>Alguna caja suya «ha vuelto» (#870); null = sin la tabla.</summary>
        public bool? HaVuelto { get; set; }
        public int EnviosRepartoPropio { get; set; }
        public DateTime? PrimerEnvioRepartoPropio { get; set; }
        /// <summary>Envío de la 14 entregado incompleto (DetalleEstado de #880 o bultos pendientes).</summary>
        public bool EntregaIncompleta { get; set; }

        /// <summary>Alguna foto de las fechas de entrega en sus primeros días con algo que no salía del stock de Algete.</summary>
        public bool FaltabaStockEnFoto { get; set; }
        /// <summary>«Quitar picking» de Nesto en Modificaciones.</summary>
        public bool PickingQuitado { get; set; }
        /// <summary>Tipos de dbo.CalidadEventos apuntados con este pedido.</summary>
        public HashSet<string> EventosCalidad { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Lo que pasó después de servir cada línea servida del pedido (<see cref="Reclamaciones.ReglaErrorEnvioReclamado"/>); lo rellena el
        /// servicio con la lectura del periodo. Vacía: no se ha podido mirar (no evaluado).
        /// </summary>
        public List<Reclamaciones.ResultadoReclamacion> ReclamacionesEnvio { get; set; } = new List<Reclamaciones.ResultadoReclamacion>();
    }

    /// <summary>Una factura del pedido que tiene rectificativa (LinFacturaVtaRectificacion.FacturaOriginalNumero), con sus unidades.</summary>
    public class FacturaRectificadaPedidoPerfecto
    {
        public string Factura { get; set; }
        /// <summary>Unidades de producto (TipoLinea 1, Cantidad &gt; 0) de ESTE pedido en esa factura.</summary>
        public decimal UnidadesFacturadas { get; set; }
        /// <summary>Unidades rectificadas cuya línea original (FacturaOriginalLinea = Nº Orden) es una línea de producto de este pedido en esa factura.</summary>
        public decimal UnidadesRectificadasDelPedido { get; set; }
        /// <summary>Unidades rectificadas de esa factura cuya línea original no es ninguna línea de la factura (rastro antiguo o mal enlazado).</summary>
        public decimal UnidadesRectificadasSinLinea { get; set; }
        /// <summary>Cuántos pedidos distintos lleva la factura (las «sin línea» solo se le atribuyen al pedido si es el único).</summary>
        public int PedidosDeLaFactura { get; set; }
    }

    /// <summary>Lo que no depende del pedido: hoy, los festivos y desde cuándo hay rastro de cada cosa (null = no hay).</summary>
    public class ContextoPedidoPerfecto
    {
        public DateTime Hoy { get; set; }
        /// <summary>(día, almacén) → no se trabaja. En producción, <c>GestorFestivos.EsFestivo</c>.</summary>
        public Func<DateTime, string, bool> EsFestivo { get; set; }
        public DateTime? FotosStockDesde { get; set; }
        public DateTime? QuitarPickingDesde { get; set; }
        public DateTime? EntregaIncompletaDesde { get; set; }
        /// <summary>Primer evento de calidad de cada tipo (dbo.CalidadEventos); vacío sin la tabla.</summary>
        public Dictionary<string, DateTime> PrimerEventoPorTipo { get; set; } = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public DateTime? EvaluadoDesde(MotivoPedidoPerfecto motivo)
        {
            switch (motivo)
            {
                case MotivoPedidoPerfecto.FaltabaStock:
                    return FotosStockDesde?.Date;
                case MotivoPedidoPerfecto.TocadoTrasElPicking:
                    return Minimo(QuitarPickingDesde, PrimerEvento(TiposEventoCalidad.TOCADO_TRAS_EL_PICKING));
                case MotivoPedidoPerfecto.EnvioDeRutaPropiaForzado:
                    return PrimerEvento(TiposEventoCalidad.RUTA_PROPIA_FORZADA);
                case MotivoPedidoPerfecto.EntregaIncompletaDelReparto:
                    return EntregaIncompletaDesde?.Date;
                case MotivoPedidoPerfecto.PidioCambiarAlgoConPicking:
                    return PrimerEvento(TiposEventoCalidad.PIDIO_CAMBIAR_CON_PICKING);
                default:
                    return null;
            }
        }

        /// <summary>¿El motivo depende de un rastro que empieza en una fecha?</summary>
        public static bool TieneFechaDeInicio(MotivoPedidoPerfecto motivo) =>
            motivo == MotivoPedidoPerfecto.FaltabaStock || motivo == MotivoPedidoPerfecto.TocadoTrasElPicking
            || motivo == MotivoPedidoPerfecto.EnvioDeRutaPropiaForzado || motivo == MotivoPedidoPerfecto.EntregaIncompletaDelReparto
            || motivo == MotivoPedidoPerfecto.PidioCambiarAlgoConPicking;

        private DateTime? PrimerEvento(IEnumerable<string> tipos)
        {
            DateTime? primero = null;
            foreach (string tipo in tipos)
            {
                if (PrimerEventoPorTipo != null && PrimerEventoPorTipo.TryGetValue(tipo, out DateTime fecha))
                {
                    primero = Minimo(primero, fecha.Date);
                }
            }
            return primero;
        }

        private static DateTime? Minimo(DateTime? a, DateTime? b) => !a.HasValue ? b : !b.HasValue ? a : (a.Value <= b.Value ? a : b);
    }

    public class EvaluacionPedidoPerfecto
    {
        public List<MotivoPedidoPerfecto> Motivos { get; set; } = new List<MotivoPedidoPerfecto>();
        /// <summary>Los motivos que para ESTE pedido no se han podido mirar (no se sabe): no cuentan ni a favor ni en contra.</summary>
        public HashSet<MotivoPedidoPerfecto> NoEvaluados { get; set; } = new HashSet<MotivoPedidoPerfecto>();
        public int? DiasLaborablesHastaServir { get; set; }
        public string Canal { get; set; }
        public bool Perfecto => Motivos.Count == 0;
        public bool PerfectoSinContarInformativos => !Motivos.Any(InfoMotivosPedidoPerfecto.EsError);
    }

    /// <summary>LA REGLA (pura): los motivos por los que un pedido no fue perfecto, con sus hechos ya leídos.</summary>
    public static class ReglaPedidoPerfecto
    {
        /// <summary>Sin servir nada pasados estos días naturales (los mismos 21 que deja por defecto el periodo) = «Sin servir».</summary>
        public const int DIAS_PLAZO_SIN_SERVIR = 21;
        /// <summary>Del día del pedido al primer albarán, como mucho un laborable (pedido del viernes servido el lunes = perfecto).</summary>
        public const int MAXIMO_DIAS_LABORABLES = 1;
        /// <summary>Las líneas «sin fecha» (notas, retenidas) nacen a 31/12/2099.</summary>
        public static readonly DateTime FECHA_SIN_FECHA = new DateTime(2099, 1, 1);

        public static class Canales
        {
            public const string VENDEDOR = "Vendedor";
            public const string TIENDA_ONLINE = "TiendaOnline";
            public const string AMAZON = "Amazon";
            public const string OTRO_CANAL_EXTERNO = "OtroCanalExterno";
            public const string APP = "App";
            public const string TIENDA_FISICA = "TiendaFisica";
            public const string CURSOS = "Cursos";
            public const string INTERNO = "Interno";
            public const string OTROS = "Otros";
        }

        public static EvaluacionPedidoPerfecto Evaluar(HechosPedidoPerfecto h, ContextoPedidoPerfecto contexto)
        {
            if (h == null)
            {
                throw new ArgumentNullException(nameof(h));
            }
            if (contexto == null)
            {
                throw new ArgumentNullException(nameof(contexto));
            }
            var evaluacion = new EvaluacionPedidoPerfecto { Canal = Canal(h) };
            Func<DateTime, string, bool> esFestivo = contexto.EsFestivo ?? ((d, a) => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday);
            DateTime fecha = h.Fecha.Date;
            bool sinFecha = h.MinFechaEntrega.HasValue && h.MinFechaEntrega.Value >= FECHA_SIN_FECHA;
            DateTime referencia = Referencia(h);

            void Poner(MotivoPedidoPerfecto motivo, bool? valor)
            {
                if (!valor.HasValue)
                {
                    _ = evaluacion.NoEvaluados.Add(motivo);
                }
                else if (valor.Value)
                {
                    evaluacion.Motivos.Add(motivo);
                }
            }

            bool? DesdeRastro(MotivoPedidoPerfecto motivo, Func<bool> valor)
            {
                DateTime? desde = contexto.EvaluadoDesde(motivo);
                return desde.HasValue && fecha >= desde.Value ? valor() : (bool?)null;
            }

            // 1. De una vez: un solo albarán y nada pendiente después de servir algo
            Poner(MotivoPedidoPerfecto.NoServidoDeUnaVez, h.LineasServidas > 0 && (h.Albaranes > 1 || h.LineasPendientes > 0));

            // 2. Sin servir: nada en albarán pasado el plazo (desde la fecha que pidió el cliente)
            bool? sinServir;
            if (h.LineasServidas > 0)
            {
                sinServir = false;
            }
            else if (h.LineasPendientes == 0)
            {
                sinServir = false; // nada pendiente ni servido (líneas en otro estado): no es «sin servir»
            }
            else if (sinFecha || contexto.Hoy.Date < referencia.AddDays(DIAS_PLAZO_SIN_SERVIR))
            {
                sinServir = null;
            }
            else
            {
                sinServir = true;
            }
            Poner(MotivoPedidoPerfecto.SinServir, sinServir);

            // 3. Al día siguiente laborable (con los festivos de su almacén)
            if (h.PrimerAlbaran.HasValue && h.ModoServicio != Constantes.Pedidos.ModosServicio.JUNTO_CON_LO_PENDIENTE && !sinFecha)
            {
                int dias = DiasLaborables(referencia, h.PrimerAlbaran.Value.Date, string.IsNullOrWhiteSpace(h.Almacen) ? Constantes.Almacenes.ALGETE : h.Almacen.Trim(), esFestivo);
                evaluacion.DiasLaborablesHastaServir = dias;
                Poner(MotivoPedidoPerfecto.NoSalioAlDiaSiguiente, dias > MAXIMO_DIAS_LABORABLES);
            }
            else
            {
                if (h.PrimerAlbaran.HasValue)
                {
                    evaluacion.DiasLaborablesHastaServir = DiasLaborables(fecha, h.PrimerAlbaran.Value.Date,
                        string.IsNullOrWhiteSpace(h.Almacen) ? Constantes.Almacenes.ALGETE : h.Almacen.Trim(), esFestivo);
                }
                Poner(MotivoPedidoPerfecto.NoSalioAlDiaSiguiente, null);
            }

            // 4. Stock al entrar: solo con fotos
            Poner(MotivoPedidoPerfecto.FaltabaStock, DesdeRastro(MotivoPedidoPerfecto.FaltabaStock, () => h.FaltabaStockEnFoto));

            // 5. Contado (informativo)
            Poner(MotivoPedidoPerfecto.NoCobradoAlContado, string.IsNullOrWhiteSpace(h.PlazosPago) ? (bool?)null : !EsContado(h.PlazosPago));

            // 6. Antes «rectificativa, abono o copia», partido en cuatro (Carlos 27/11/26). Los tres primeros son error, como lo era el antiguo.
            Poner(MotivoPedidoPerfecto.EntregaIncompleta, h.TieneNotaEntregaPendiente);
            List<FacturaRectificadaPedidoPerfecto> rectificadas = h.FacturasRectificadas ?? new List<FacturaRectificadaPedidoPerfecto>();
            bool entera = rectificadas.Any(EsRectificadaEntera);
            Poner(MotivoPedidoPerfecto.FacturaRectificadaEntera, entera);
            Poner(MotivoPedidoPerfecto.FacturaRectificadaEnParte, !entera && rectificadas.Count > 0);
            Poner(MotivoPedidoPerfecto.TieneCopia, h.TieneCopia);

            // 7. Impagado
            Poner(MotivoPedidoPerfecto.Impagado, h.Impagado);

            // 8. Agencia
            Poner(MotivoPedidoPerfecto.IncidenciaDeAgenciaOReenvio,
                h.EnviosSalidos > 1 || h.EnvioConIncidencia || h.Reenvio == true || h.HaVuelto == true);

            // 9. Tocado tras el picking
            Poner(MotivoPedidoPerfecto.TocadoTrasElPicking, DesdeRastro(MotivoPedidoPerfecto.TocadoTrasElPicking,
                () => h.PickingQuitado || Alguno(h.EventosCalidad, TiposEventoCalidad.TOCADO_TRAS_EL_PICKING)));

            // 10. Ruta propia forzada (solo con eventos)
            Poner(MotivoPedidoPerfecto.EnvioDeRutaPropiaForzado, DesdeRastro(MotivoPedidoPerfecto.EnvioDeRutaPropiaForzado,
                () => Alguno(h.EventosCalidad, TiposEventoCalidad.RUTA_PROPIA_FORZADA)));

            // 11. Entrega incompleta del reparto propio: sin envío de la 14 no aplica (no es un problema)
            bool? incompleta;
            if (h.EnviosRepartoPropio == 0)
            {
                incompleta = false;
            }
            else
            {
                DateTime? desde = contexto.EvaluadoDesde(MotivoPedidoPerfecto.EntregaIncompletaDelReparto);
                DateTime cuando = (h.PrimerEnvioRepartoPropio ?? h.Fecha).Date;
                incompleta = desde.HasValue && cuando >= desde.Value ? h.EntregaIncompleta : (bool?)null;
            }
            Poner(MotivoPedidoPerfecto.EntregaIncompletaDelReparto, incompleta);

            // 12. Pidió cambiar algo con picking
            Poner(MotivoPedidoPerfecto.PidioCambiarAlgoConPicking, DesdeRastro(MotivoPedidoPerfecto.PidioCambiarAlgoConPicking,
                () => Alguno(h.EventosCalidad, TiposEventoCalidad.PIDIO_CAMBIAR_CON_PICKING)));

            // 17. Error de envío reclamado (Carlos 27/11/26): reenviado sin coste o cambiado por otro en alguna línea servida. Puede ir con
            // FacturaRectificadaEntera/EnParte (el cambio por otro siempre tiene rectificativa): son dos motivos del mismo pedido.
            Poner(MotivoPedidoPerfecto.ErrorDeEnvioReclamado, Reclamaciones.ReglaErrorEnvioReclamado.Combinar(h.ReclamacionesEnvio));

            evaluacion.Motivos.Sort();
            return evaluacion;
        }

        /// <summary>
        /// ¿El pedido hijo (PedidoOrigen = el evaluado) es la nota de entrega de lo pendiente? La marca firme es <c>NotaEntrega</c> = 1 del hijo:
        /// el único sitio que rellena PedidoOrigen es <c>CreadorNotaEntregaPendiente</c> (#542), y siempre con NotaEntrega = 1 (la copia de
        /// pedidos NO lo rellena). El comentario «NOTA DE ENTREGA: pendiente de entregar del pedido N» que deja, solo como respaldo.
        /// </summary>
        public static bool EsNotaEntregaDeLoPendiente(bool notaEntrega, bool comentarioDeNotaDelPedido) => notaEntrega || comentarioDeNotaDelPedido;

        /// <summary>Unidades rectificadas que se le atribuyen al pedido en esa factura: las de sus líneas y, si la factura es solo suya, las sin línea.</summary>
        public static decimal UnidadesRectificadas(FacturaRectificadaPedidoPerfecto f) =>
            f == null ? 0 : f.UnidadesRectificadasDelPedido + (f.PedidosDeLaFactura <= 1 ? f.UnidadesRectificadasSinLinea : 0);

        /// <summary>Entera: unidades rectificadas ≥ unidades de producto facturadas en esa factura para ese pedido (y alguna facturada).</summary>
        public static bool EsRectificadaEntera(FacturaRectificadaPedidoPerfecto f) =>
            f != null && f.UnidadesFacturadas > 0 && UnidadesRectificadas(f) >= f.UnidadesFacturadas;

        /// <summary>El día desde el que se cuenta: el del pedido o, si el cliente pidió una fecha de entrega posterior, esa.</summary>
        public static DateTime Referencia(HechosPedidoPerfecto h)
        {
            DateTime fecha = h.Fecha.Date;
            if (h.MinFechaEntrega.HasValue && h.MinFechaEntrega.Value < FECHA_SIN_FECHA && h.MinFechaEntrega.Value.Date > fecha)
            {
                return h.MinFechaEntrega.Value.Date;
            }
            return fecha;
        }

        /// <summary>
        /// Días laborables d con referencia &lt; d ≤ servido. Un pedido del viernes servido el lunes = 1; uno del sábado servido el lunes = 1
        /// (el sábado no se trabaja: el lunes es el primero); servido antes de la referencia = 0.
        /// </summary>
        public static int DiasLaborables(DateTime referencia, DateTime servido, string almacen, Func<DateTime, string, bool> esFestivo)
        {
            int dias = 0;
            for (DateTime d = referencia.Date.AddDays(1); d <= servido.Date; d = d.AddDays(1))
            {
                if (!EsNoLaborable(d, almacen, esFestivo))
                {
                    dias++;
                }
            }
            if (dias > 0 && EsNoLaborable(referencia.Date, almacen, esFestivo))
            {
                // Pedido metido en un día que no se trabaja: el primer laborable es «el día del pedido», no el siguiente
                dias--;
            }
            return dias;
        }

        private static bool EsNoLaborable(DateTime dia, string almacen, Func<DateTime, string, bool> esFestivo) =>
            dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday || esFestivo(dia, almacen);

        /// <summary>Contado, contado riguroso o prepago (Constantes.PlazosPago). Lo demás (1/30, 2/30, E1/30…) son plazos.</summary>
        public static bool EsContado(string plazosPago)
        {
            string p = plazosPago?.Trim();
            return string.Equals(p, Constantes.PlazosPago.CONTADO, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p, Constantes.PlazosPago.CONTADO_RIGUROSO, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p, Constantes.PlazosPago.PREPAGO, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// El canal, con lo que ya sabe el código: clientes especiales (Constantes.ClientesEspeciales), forma de venta de las líneas
        /// (Constantes.FormasVenta) y almacén de las líneas (REI/ALC = tienda). El primero que case manda.
        /// </summary>
        public static string Canal(HechosPedidoPerfecto h)
        {
            string cliente = h.Cliente?.Trim();
            if (cliente == Constantes.ClientesEspeciales.EL_EDEN || cliente == Constantes.ClientesEspeciales.MATERIALES_CURSOS)
            {
                return Canales.INTERNO;
            }
            if (h.LineasAmazon > 0 || cliente == Constantes.ClientesEspeciales.AMAZON)
            {
                return Canales.AMAZON;
            }
            if (h.LineasTiendaOnline > 0 || cliente == Constantes.ClientesEspeciales.TIENDA_ONLINE)
            {
                return Canales.TIENDA_ONLINE;
            }
            if (h.LineasOtroCanalExterno > 0)
            {
                return Canales.OTRO_CANAL_EXTERNO;
            }
            if (h.LineasApp > 0)
            {
                return Canales.APP;
            }
            if (h.LineasProducto > 0 && h.LineasCursos == h.LineasProducto)
            {
                return Canales.CURSOS;
            }
            if (h.LineasEnTienda > 0 || cliente == Constantes.ClientesEspeciales.PEDIDO_TIENDA)
            {
                return Canales.TIENDA_FISICA;
            }
            if (cliente == Constantes.ClientesEspeciales.PUBLICO_FINAL)
            {
                return Canales.OTROS;
            }
            return Canales.VENDEDOR;
        }

        private static bool Alguno(HashSet<string> eventos, IEnumerable<string> tipos) =>
            eventos != null && tipos.Any(eventos.Contains);
    }
}
