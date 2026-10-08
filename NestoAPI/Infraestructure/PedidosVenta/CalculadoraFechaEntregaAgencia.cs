using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#606: una línea de producto de Algete con el reparto de sus unidades según de dónde van a salir. Lo que no
    /// cubren <see cref="EnAlgete"/>, <see cref="EnTiendas"/>, <see cref="EnCaminoDeTiendas"/> y <see cref="DelProveedor"/>
    /// no tiene fecha.
    /// </summary>
    public class LineaFechaEntregaAgencia
    {
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        /// <summary>0 = regalo (Ganavisiones, regalo por importe, material promocional): NestoAPI#529.</summary>
        public decimal BaseImponible { get; set; }
        /// <summary>Fecha de entrega de la línea (entrega futura). Null = la de hoy.</summary>
        public DateTime? FechaEntrega { get; set; }
        /// <summary>La línea ya tiene picking: sale con él, hoy.</summary>
        public bool YaEnPicking { get; set; }
        /// <summary>Unidades libres en Algete (stock − lo que deben los demás pedidos).</summary>
        public int EnAlgete { get; set; }
        /// <summary>Unidades que hay que traer de cada tienda (código de almacén → unidades).</summary>
        public Dictionary<string, int> EnTiendas { get; set; } = new Dictionary<string, int>();
        /// <summary>Unidades que ya vienen de camino a Algete en una reposición generada y sin recibir.</summary>
        public int EnCaminoDeTiendas { get; set; }
        /// <summary>Unidades de un pedido a proveedor enviado (LinPedidoCmp) con su fecha prevista.</summary>
        public int DelProveedor { get; set; }
        public DateTime? FechaProveedor { get; set; }
        /// <summary>Texto que acompaña a lo que no tiene fecha (p. ej. «sobre pedido»). Opcional.</summary>
        public string MotivoSinFecha { get; set; }
    }

    /// <summary>NestoAPI#606: todo lo que necesita <see cref="CalculadoraFechaEntregaAgencia"/>.</summary>
    public class EntradaFechaEntregaAgencia
    {
        public string Empresa { get; set; } = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        public List<LineaFechaEntregaAgencia> Lineas { get; set; } = new List<LineaFechaEntregaAgencia>();
        /// <summary>Modo de servicio EFECTIVO (<see cref="Constantes.Pedidos.ModosServicio"/>).</summary>
        public byte ModoServicio { get; set; } = Constantes.Pedidos.ModosServicio.POR_DEFECTO;
        /// <summary>Modo de facturación EFECTIVO (<see cref="Constantes.Pedidos.ModosFacturacion"/>).</summary>
        public byte ModoFacturacion { get; set; } = Constantes.Pedidos.ModosFacturacion.POR_ENTREGAS;
        public string Ruta { get; set; }
        /// <summary>Clientes.DiasEnServir ("11111"): el picking no saca un pedido cuya entrega cae en un día que cierra (#362).</summary>
        public string DiasEnServir { get; set; }
        /// <summary>El pedido ya tuvo alguna entrega (en el modo 4, lo que queda va de una vez).</summary>
        public bool TieneLineasServidas { get; set; }
        public DateTime Ahora { get; set; }
        public TimeSpan HoraCorte { get; set; } = HoraCortePicking.POR_DEFECTO;
        /// <summary>Calendario de reposiciones (ReposicionesCalendario).</summary>
        public IEnumerable<ReposicionCalendario> Calendario { get; set; } = new List<ReposicionCalendario>();
    }

    /// <summary>NestoAPI#606: cuándo entregamos el pedido a la agencia.</summary>
    public class ResultadoFechaEntregaAgencia
    {
        /// <summary>Día (sin hora) de la primera entrega a la agencia. Null = no se sabe (o no sale nada por agencia).</summary>
        public DateTime? PrimeraEntrega { get; set; }
        /// <summary>Día de la entrega con la que queda todo servido. Null = algo no tiene fecha.</summary>
        public DateTime? EntregaCompleta { get; set; }
        /// <summary>Todos los días en que sale algo, en orden.</summary>
        public List<DateTime> Entregas { get; set; } = new List<DateTime>();
        /// <summary>La que se enseña según el modo: «Todo junto» → la completa; el resto → la primera.</summary>
        public bool AplicaCompleta { get; set; }
        public DateTime? FechaQueAplica => AplicaCompleta ? EntregaCompleta : PrimeraEntrega;
        /// <summary>Por qué, en castellano, para el tooltip.</summary>
        public string Motivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#606: qué día entregamos el pedido a la agencia (el día del picking que lo saca: el picking de cierre es a la
    /// hora de corte y la agencia recoge esa tarde). Pura: sin BD ni reloj. REPLICA las reglas del picking
    /// (<see cref="GestorPicking"/>), no las inventa: se simula una pasada por cada día laborable de Algete.
    /// <list type="number">
    /// <item>Primer día posible de cada línea: <c>GestorPedidosVenta.FechaEntregaAjustada</c> (hora de corte, ruta con o sin
    /// portes, festivos de Algete, su fecha de entrega si es futura). Antes de ese día la línea no está en el picking
    /// (<c>BorrarLineasEntregaFutura</c>).</item>
    /// <item>Cuándo está cada unidad en Algete: libres → ya; en una tienda → el día en que sale el pedido que espera la
    /// próxima reposición de esa tienda (<see cref="CalculadoraFechaReposicion"/>); en camino desde una tienda → el
    /// laborable siguiente a hoy; del proveedor → el laborable siguiente a su fecha prevista (fecha ya pasada = sin fecha);
    /// el resto, sin fecha.</item>
    /// <item>Cada día: si la entrega (el laborable siguiente) cae en un día que el cliente cierra, no sale
    /// (<see cref="GestorDiasEnServir"/>). Modo 3: no sale mientras le quede algo por llegar de las tiendas
    /// (<see cref="GestorReposicionTiendas"/>). Exige tenerlo todo el modo 1, el 4 tras la primera entrega y cualquier
    /// modo si lo único que falta son regalos (<see cref="PedidoPicking.ExigeStockDeTodo"/>, #529). Si tiene productos de
    /// pago, tiene que salir alguno de pago (<see cref="GestorStocksPicking.HayStockDeAlgo"/>).</item>
    /// <item>Facturación «todo ahora» (<see cref="GestorFacturarTodoAhora"/>): en su primera pasada sale lo que hay (nada, si
    /// el modo exige tenerlo todo y falta algo) y lo demás se va a la nota de entrega, que nace SIN fecha
    /// (<c>CreadorNotaEntregaPendiente.FECHA_ENTREGA_SIN_DETERMINAR</c>). «Por entregas» y «al completar» no cambian
    /// cuándo sale la mercancía, solo cuándo se factura.</item>
    /// </list>
    /// No modela la retención por prepago ni el importe mínimo de portes (no retienen: solo añaden portes).
    /// </summary>
    public class CalculadoraFechaEntregaAgencia
    {
        private const int DIAS_MARGEN_HORIZONTE = 31;
        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        private readonly Func<DateTime, string, bool> esFestivo;
        private readonly CalculadoraFechaReposicion calculadoraReposicion;

        public CalculadoraFechaEntregaAgencia() : this(GestorFestivos.EsFestivo)
        {
        }

        /// <param name="esFestivo">(día, almacén) → true si ese almacén no trabaja ese día.</param>
        public CalculadoraFechaEntregaAgencia(Func<DateTime, string, bool> esFestivo)
        {
            this.esFestivo = esFestivo ?? throw new ArgumentNullException(nameof(esFestivo));
            calculadoraReposicion = new CalculadoraFechaReposicion(esFestivo);
        }

        public ResultadoFechaEntregaAgencia Calcular(EntradaFechaEntregaAgencia entrada)
        {
            if (entrada == null)
            {
                throw new ArgumentNullException(nameof(entrada));
            }
            byte servicio = entrada.ModoServicio;
            bool todoAhora = Constantes.Pedidos.ModosFacturacion.EsTodoAhora(entrada.ModoFacturacion);
            DateTime hoy = entrada.Ahora.Date;
            var resultado = new ResultadoFechaEntregaAgencia
            {
                AplicaCompleta = Constantes.Pedidos.ModosServicio.EsTodoJunto(servicio)
            };
            var avisos = new List<string>();

            List<Lote> lotes = CrearLotes(entrada, hoy, avisos);
            var entregas = new SortedSet<DateTime>();

            // Lo que ya tiene picking sale con él, hoy.
            List<Lote> enPicking = lotes.Where(l => l.YaEnPicking).ToList();
            if (enPicking.Any())
            {
                _ = entregas.Add(hoy);
                enPicking.ForEach(l => l.Enviado = true);
            }
            bool servido = entrada.TieneLineasServidas || enPicking.Any();

            if (!lotes.Any())
            {
                resultado.Motivo = "El pedido no tiene productos pendientes que salgan de Algete por agencia.";
                return resultado;
            }

            DateTime inicio = lotes.Where(l => !l.Enviado).Select(l => l.Elegible).DefaultIfEmpty(hoy).Min();
            DateTime ultimaConocida = lotes.Select(l => l.Elegible)
                .Concat(lotes.Where(l => l.Disponible.HasValue).Select(l => l.Disponible.Value))
                .DefaultIfEmpty(hoy).Max();
            DateTime horizonte = ultimaConocida.AddDays(DIAS_MARGEN_HORIZONTE);
            bool esperoTiendas = false;
            bool esperoRegalos = false;
            bool fueANota = false;
            bool todoANota = false;
            var diasCerrados = new List<DateTime>();

            for (DateTime dia = inicio; dia <= horizonte && lotes.Any(l => l.Pendiente); dia = dia.AddDays(1))
            {
                if (!EsLaborable(dia))
                {
                    continue;
                }
                List<Lote> pendientes = lotes.Where(l => l.Pendiente).ToList();
                List<Lote> considerados = pendientes.Where(l => l.Elegible <= dia).ToList();
                if (!considerados.Any())
                {
                    continue;
                }

                // #362: si la entrega (el laborable siguiente a la salida) cae en un día que el cliente cierra, no sale.
                DateTime diaEntrega = GestorDiasEnServir.CalcularDiaEntrega(dia, f => !EsLaborable(f));
                if (!GestorDiasEnServir.EstaAbierto(entrada.DiasEnServir, diaEntrega))
                {
                    diasCerrados.Add(diaEntrega);
                    continue;
                }

                // Modo 3 (GestorReposicionTiendas.MarcarEsperas, sobre TODAS sus líneas): no sale mientras le quede algo
                // suyo por llegar de las tiendas.
                if (servicio == Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS
                    && pendientes.Any(l => l.DeTienda && !EstaEnAlgete(l, dia)))
                {
                    esperoTiendas = true;
                    continue;
                }

                List<Lote> disponibles = considerados.Where(l => EstaEnAlgete(l, dia)).ToList();
                List<Lote> faltan = considerados.Where(l => !EstaEnAlgete(l, dia)).ToList();
                bool loQueFaltaSonRegalos = faltan.Any() && faltan.All(l => l.EsRegalo);
                bool exigeTodo = servicio == Constantes.Pedidos.ModosServicio.TODO_JUNTO
                    || (servicio == Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ && servido)
                    || loQueFaltaSonRegalos;

                if (todoAhora)
                {
                    // GestorFacturarTodoAhora: se factura todo en esta pasada; lo que no sale va a la nota (sin fecha).
                    if (exigeTodo && faltan.Any())
                    {
                        considerados.ForEach(l => l.ANota = true);
                        todoANota = true;
                    }
                    else
                    {
                        if (disponibles.Any())
                        {
                            _ = entregas.Add(dia);
                            disponibles.ForEach(l => l.Enviado = true);
                            servido = true;
                        }
                        faltan.ForEach(l => l.ANota = true);
                    }
                    fueANota |= considerados.Any(l => l.ANota);
                    continue;
                }

                if (exigeTodo && faltan.Any())
                {
                    esperoRegalos |= loQueFaltaSonRegalos && servicio != Constantes.Pedidos.ModosServicio.TODO_JUNTO;
                    continue;
                }
                if (!disponibles.Any())
                {
                    continue;
                }
                // HayStockDeAlgo: con productos de pago en el pedido, tiene que salir alguno de pago.
                if (considerados.Any(l => !l.EsRegalo) && !disponibles.Any(l => !l.EsRegalo))
                {
                    continue;
                }
                _ = entregas.Add(dia);
                disponibles.ForEach(l => l.Enviado = true);
                servido = true;
            }

            resultado.Entregas = entregas.ToList();
            resultado.PrimeraEntrega = entregas.Any() ? entregas.Min : (DateTime?)null;
            resultado.EntregaCompleta = entregas.Any() && lotes.All(l => l.Enviado) ? entregas.Max : (DateTime?)null;
            resultado.Motivo = Motivo(entrada, resultado, lotes, avisos, esperoTiendas, esperoRegalos, fueANota, todoANota, diasCerrados);
            return resultado;
        }

        private static bool EstaEnAlgete(Lote lote, DateTime dia) => lote.Disponible.HasValue && lote.Disponible.Value <= dia;

        private List<Lote> CrearLotes(EntradaFechaEntregaAgencia entrada, DateTime hoy, List<string> avisos)
        {
            var lotes = new List<Lote>();
            var salidaPorTienda = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
            foreach (LineaFechaEntregaAgencia linea in (entrada.Lineas ?? new List<LineaFechaEntregaAgencia>()).Where(l => l != null && l.Cantidad > 0))
            {
                DateTime fechaLinea = linea.FechaEntrega.HasValue && linea.FechaEntrega.Value > DateTime.MinValue ? linea.FechaEntrega.Value : hoy;
                DateTime elegible = GestorPedidosVenta.FechaEntregaAjustada(fechaLinea, entrada.Ruta, Constantes.Almacenes.ALGETE,
                    entrada.Ahora, entrada.HoraCorte, esFestivo);
                bool regalo = linea.BaseImponible == 0;
                string producto = linea.Producto?.Trim();

                Lote Nuevo(int unidades, DateTime? disponible, string origen, string tienda = null, string sinFecha = null)
                {
                    return new Lote
                    {
                        Producto = producto,
                        Unidades = unidades,
                        Elegible = elegible,
                        Disponible = disponible,
                        Origen = origen,
                        Tienda = tienda,
                        EsRegalo = regalo,
                        MotivoSinFecha = sinFecha
                    };
                }

                if (linea.YaEnPicking)
                {
                    Lote lote = Nuevo(linea.Cantidad, hoy, ORIGEN_PICKING);
                    lote.YaEnPicking = true;
                    lotes.Add(lote);
                    continue;
                }

                int resto = linea.Cantidad;
                int Tomar(int unidades)
                {
                    int tomadas = Math.Min(resto, Math.Max(0, unidades));
                    resto -= tomadas;
                    return tomadas;
                }

                int algete = Tomar(linea.EnAlgete);
                if (algete > 0)
                {
                    lotes.Add(Nuevo(algete, hoy, ORIGEN_ALGETE));
                }
                foreach (KeyValuePair<string, int> tienda in (linea.EnTiendas ?? new Dictionary<string, int>()).OrderBy(t => t.Key))
                {
                    int unidades = Tomar(tienda.Value);
                    if (unidades <= 0)
                    {
                        continue;
                    }
                    string codigo = tienda.Key?.Trim().ToUpperInvariant();
                    if (!salidaPorTienda.TryGetValue(codigo, out DateTime? sale))
                    {
                        sale = calculadoraReposicion.Calcular(entrada.Calendario, codigo, Constantes.Almacenes.ALGETE, entrada.HoraCorte,
                            entrada.Ahora, entrada.Empresa)?.PedidoSaleEl.Date;
                        salidaPorTienda[codigo] = sale;
                    }
                    Lote lote = Nuevo(unidades, sale, ORIGEN_TIENDA, codigo,
                        sale.HasValue ? null : $"no hay calendario de reposición desde {NombreAlmacen(codigo)}");
                    lote.DeTienda = true;
                    lotes.Add(lote);
                }
                int enCamino = Tomar(linea.EnCaminoDeTiendas);
                if (enCamino > 0)
                {
                    Lote lote = Nuevo(enCamino, SiguienteLaborable(hoy), ORIGEN_EN_CAMINO);
                    lote.DeTienda = true;
                    lotes.Add(lote);
                }
                int proveedor = Tomar(linea.DelProveedor);
                if (proveedor > 0)
                {
                    DateTime? prevista = linea.FechaProveedor?.Date;
                    if (prevista.HasValue && prevista.Value >= hoy)
                    {
                        Lote lote = Nuevo(proveedor, SiguienteLaborable(prevista.Value), ORIGEN_PROVEEDOR);
                        lote.FechaProveedor = prevista;
                        lotes.Add(lote);
                    }
                    else
                    {
                        lotes.Add(Nuevo(proveedor, null, ORIGEN_PROVEEDOR, sinFecha: prevista.HasValue
                            ? $"el pedido al proveedor tenía fecha prevista el {prevista.Value.ToString("dd/MM", castellano)}, ya pasada"
                            : "el pedido al proveedor no tiene fecha prevista"));
                    }
                }
                if (resto > 0)
                {
                    lotes.Add(Nuevo(resto, null, ORIGEN_SIN_STOCK, sinFecha: string.IsNullOrWhiteSpace(linea.MotivoSinFecha)
                        ? "no hay stock ni fecha de llegada" : linea.MotivoSinFecha.Trim()));
                }
            }
            return lotes;
        }

        private string Motivo(EntradaFechaEntregaAgencia entrada, ResultadoFechaEntregaAgencia resultado, List<Lote> lotes, List<string> avisos,
            bool esperoTiendas, bool esperoRegalos, bool fueANota, bool todoANota, List<DateTime> diasCerrados)
        {
            var frases = new List<string>();
            if (resultado.PrimeraEntrega.HasValue && resultado.EntregaCompleta == resultado.PrimeraEntrega)
            {
                frases.Add($"Sale entero a la agencia el {Dia(resultado.PrimeraEntrega.Value)}.");
            }
            else if (resultado.PrimeraEntrega.HasValue)
            {
                frases.Add($"Primera entrega a la agencia el {Dia(resultado.PrimeraEntrega.Value)}; " +
                    (resultado.EntregaCompleta.HasValue
                        ? $"entrega completa el {Dia(resultado.EntregaCompleta.Value)} ({resultado.Entregas.Count} entregas)."
                        : "la entrega completa no tiene fecha."));
            }
            else
            {
                frases.Add("Sin fecha de entrega a la agencia.");
            }

            frases.Add($"Modo «{Constantes.Pedidos.ModosServicio.Nombre(entrada.ModoServicio)}»: {ExplicacionModo(entrada.ModoServicio)}");

            DateTime hoy = entrada.Ahora.Date;
            DateTime primerPicking = GestorPedidosVenta.FechaEntregaAjustada(hoy, entrada.Ruta, Constantes.Almacenes.ALGETE, entrada.Ahora,
                entrada.HoraCorte, esFestivo);
            List<Lote> porSalir = lotes.Where(l => !l.YaEnPicking).ToList();
            DateTime? primeraElegible = porSalir.Any() ? porSalir.Min(l => l.Elegible) : (DateTime?)null;
            if (primeraElegible.HasValue && primeraElegible.Value > primerPicking)
            {
                frases.Add($"Tiene fecha de entrega el {Dia(primeraElegible.Value)}: no entra en el picking hasta ese día.");
            }
            else if (primeraElegible.HasValue && primerPicking > hoy)
            {
                frases.Add(entrada.Ahora.TimeOfDay >= entrada.HoraCorte && EsLaborable(hoy)
                    ? $"Pasada la hora de corte ({entrada.HoraCorte:hh\\:mm}), el primer picking es el {Dia(primerPicking)}."
                    : $"El primer picking es el {Dia(primerPicking)}.");
            }
            if (primeraElegible.HasValue && porSalir.Any(l => l.Elegible > primeraElegible.Value))
            {
                frases.Add("Alguna línea tiene una fecha de entrega posterior: no entra en el picking hasta ese día.");
            }
            if (lotes.Any(l => l.YaEnPicking))
            {
                frases.Add("Lo que ya tiene picking sale hoy.");
            }
            if (lotes.Any(l => l.Origen == ORIGEN_ALGETE))
            {
                frases.Add("Lo que hay en Algete está listo.");
            }
            // Una frase por producto, origen y día (dos líneas del mismo producto no repiten frase).
            var grupos = lotes.Where(l => l.Origen != ORIGEN_ALGETE && l.Origen != ORIGEN_PICKING)
                .GroupBy(l => new { l.Origen, l.Producto, l.Tienda, l.Disponible, l.FechaProveedor, l.MotivoSinFecha })
                .Select(g => new { g.Key.Origen, g.Key.Producto, g.Key.Tienda, g.Key.Disponible, g.Key.FechaProveedor, g.Key.MotivoSinFecha, Unidades = g.Sum(l => l.Unidades) })
                .ToList();
            foreach (var g in grupos.Where(g => g.Disponible.HasValue))
            {
                string n = g.Unidades == 1 ? string.Empty : "n";
                string cuantas = $"{g.Unidades} {Unidades(g.Unidades)} de {g.Producto}";
                switch (g.Origen)
                {
                    case ORIGEN_TIENDA:
                        frases.Add($"{cuantas} en {NombreAlmacen(g.Tienda)}: llega{n} con la reposición y puede{n} salir el {Dia(g.Disponible.Value)}.");
                        break;
                    case ORIGEN_EN_CAMINO:
                        frases.Add($"{cuantas} ya viene{n} de camino desde una tienda: puede{n} salir el {Dia(g.Disponible.Value)}.");
                        break;
                    case ORIGEN_PROVEEDOR:
                        frases.Add($"{cuantas} del proveedor (prevista el {g.FechaProveedor.Value.ToString("dd/MM", castellano)}): puede{n} salir el {Dia(g.Disponible.Value)}.");
                        break;
                }
            }
            foreach (var g in grupos.Where(g => !g.Disponible.HasValue))
            {
                frases.Add($"{g.Unidades} {Unidades(g.Unidades)} de {g.Producto} sin fecha: {g.MotivoSinFecha}.");
            }
            if (esperoTiendas)
            {
                frases.Add("Espera a que llegue lo de las tiendas antes de salir.");
            }
            if (esperoRegalos)
            {
                frases.Add("Lo que falta son solo regalos: no se sirve a medias, se espera a tenerlo todo.");
            }
            if (diasCerrados.Any())
            {
                string dias = string.Join(", ", diasCerrados.Distinct().Select(Dia));
                frases.Add($"El cliente cierra el {dias}: ese picking no lo saca.");
            }
            if (todoANota)
            {
                frases.Add("Se factura todo ahora y, como falta algo y el pedido va todo junto, lo pendiente pasa entero a una nota de entrega que nace sin fecha (la pone almacén).");
            }
            else if (fueANota)
            {
                frases.Add("Se factura todo ahora: lo que no sale en la primera entrega pasa a una nota de entrega que nace sin fecha (la pone almacén).");
            }
            frases.AddRange(avisos);
            return string.Join(" ", frases);
        }

        private static string ExplicacionModo(byte modo)
        {
            switch (modo)
            {
                case Constantes.Pedidos.ModosServicio.TODO_JUNTO:
                    return "sale cuando esté todo.";
                case Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO:
                    return "sale lo que hay y el resto en cuanto llegue.";
                case Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS:
                    return "espera a la reposición de las tiendas y luego sale lo que haya.";
                case Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ:
                    return "sale lo que hay y el resto en una sola entrega más.";
                default:
                    return "modo desconocido.";
            }
        }

        private static string Unidades(int n) => n == 1 ? "ud." : "uds.";

        internal static string NombreAlmacen(string codigo)
        {
            switch (codigo?.Trim().ToUpperInvariant())
            {
                case Constantes.Almacenes.REINA: return "Reina";
                case Constantes.Almacenes.ALCOBENDAS: return "Alcobendas";
                case Constantes.Almacenes.ALGETE: return "Algete";
                default: return codigo?.Trim();
            }
        }

        private static string Dia(DateTime dia) => dia.ToString("dddd dd/MM", castellano);

        private bool EsLaborable(DateTime dia)
        {
            return dia.DayOfWeek != DayOfWeek.Saturday && dia.DayOfWeek != DayOfWeek.Sunday && !esFestivo(dia, Constantes.Almacenes.ALGETE);
        }

        /// <summary>El primer laborable de Algete POSTERIOR a <paramref name="dia"/>: lo que se recibe un día sale en el picking del siguiente.</summary>
        private DateTime SiguienteLaborable(DateTime dia)
        {
            DateTime siguiente = dia.Date.AddDays(1);
            for (int i = 0; i < 60 && !EsLaborable(siguiente); i++)
            {
                siguiente = siguiente.AddDays(1);
            }
            return siguiente;
        }

        private const string ORIGEN_PICKING = "Picking";
        private const string ORIGEN_ALGETE = "Algete";
        private const string ORIGEN_TIENDA = "Tienda";
        private const string ORIGEN_EN_CAMINO = "EnCamino";
        private const string ORIGEN_PROVEEDOR = "Proveedor";
        private const string ORIGEN_SIN_STOCK = "SinStock";

        /// <summary>Unidades de una línea que llegan a Algete el mismo día.</summary>
        private class Lote
        {
            public string Producto { get; set; }
            public int Unidades { get; set; }
            /// <summary>Primer día en que la línea entra en el picking (hora de corte, ruta, fecha de entrega).</summary>
            public DateTime Elegible { get; set; }
            /// <summary>Día de picking a partir del cual las unidades están en Algete. Null = sin fecha.</summary>
            public DateTime? Disponible { get; set; }
            public string Origen { get; set; }
            public string Tienda { get; set; }
            public bool DeTienda { get; set; }
            public bool EsRegalo { get; set; }
            public bool YaEnPicking { get; set; }
            public DateTime? FechaProveedor { get; set; }
            public string MotivoSinFecha { get; set; }
            public bool Enviado { get; set; }
            /// <summary>Facturado «todo ahora» y pasado a la nota de entrega sin fecha.</summary>
            public bool ANota { get; set; }
            public bool Pendiente => !Enviado && !ANota;
        }
    }
}
