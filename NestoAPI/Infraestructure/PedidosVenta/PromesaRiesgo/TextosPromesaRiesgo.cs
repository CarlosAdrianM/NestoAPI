using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Lo que hace falta de un riesgo para avisar (la fila más el cliente y sus comerciales).</summary>
    public class AvisoPromesaRiesgo
    {
        public RiesgoPromesa Riesgo { get; set; }
        public string Cliente { get; set; }
        public string NombreCliente { get; set; }
        /// <summary>Nombres de los comerciales del pedido (para el correo).</summary>
        public string Comerciales { get; set; }
        /// <summary>True si es un riesgo que ya se avisó y ha empeorado.</summary>
        public bool Empeora { get; set; }
        /// <summary>Carlos (18/11/26): la buena noticia de un pedido ya avisado al que le ha llegado lo que faltaba (el Motivo ya lo dice).</summary>
        public bool YaLlego { get; set; }
    }

    /// <summary>
    /// NestoAPI#693: los textos, puros y de usted. Carlos (18/11/26): una o dos líneas por pedido con QUÉ FALTA (resumido), CUÁNDO LLEGA
    /// y CUÁNDO SALE; nada de lo que sí está en Algete. «Faltan 45949 JABON CARBON ACTIVO y 5 productos más (50 uds.): llegan hoy del
    /// proveedor. Sale el jueves 19/11 (aproximado). Va todo junto.» Sale del desglose del motor de #606
    /// (<see cref="FechaEntregaAgenciaDTO.Desglose"/>); la certeza en una palabra (<see cref="TextosCertezaFechaEntrega.CalificativoCorto"/>).
    /// </summary>
    public static class TextosPromesaRiesgo
    {
        public const string TIPO_NOTIFICACION = "PromesaEnRiesgo";
        /// <summary>Sin culpables y sin saber qué faltaba.</summary>
        public const string TODO_EN_ALGETE = "Ya está todo en Algete: sale en el próximo picking.";
        /// <summary>Largo máximo del nombre del producto en la frase (se corta por palabra).</summary>
        public const int LARGO_NOMBRE = 30;
        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        /// <summary>«jueves 22/10/2026» o «sin fecha».</summary>
        public static string Fecha(DateTime? fecha) => fecha.HasValue ? GestorPresupuestos.DiaCorreo(fecha.Value.Date) : "sin fecha";

        /// <summary>«hoy», «mañana», «19/11» o, de otro año, «08/01/2027».</summary>
        public static string DiaCorto(DateTime dia, DateTime hoy)
        {
            if (dia.Date == hoy.Date)
            {
                return "hoy";
            }
            if (dia.Date == hoy.Date.AddDays(1))
            {
                return "mañana";
            }
            return Numeros(dia, hoy);
        }

        /// <summary>«hoy», «mañana», «el jueves 19/11» o, de otro año, «el viernes 08/01/2027».</summary>
        public static string ElDia(DateTime dia, DateTime hoy)
        {
            if (dia.Date == hoy.Date || dia.Date == hoy.Date.AddDays(1))
            {
                return DiaCorto(dia, hoy);
            }
            return $"el {castellano.DateTimeFormat.GetDayName(dia.DayOfWeek)} {Numeros(dia, hoy)}";
        }

        private static string Numeros(DateTime dia, DateTime hoy) =>
            dia.Year == hoy.Year ? dia.ToString("dd/MM", CultureInfo.InvariantCulture) : dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        /// <summary>El nombre recortado a <see cref="LARGO_NOMBRE"/> letras por palabra («JABON CARBON ACTIVO DETOXIFICANTE» → «JABON CARBON ACTIVO»).</summary>
        public static string NombreCorto(string nombre)
        {
            string limpio = nombre?.Trim();
            if (string.IsNullOrEmpty(limpio) || limpio.Length <= LARGO_NOMBRE)
            {
                return limpio;
            }
            string corte = limpio.Substring(0, LARGO_NOMBRE + 1);
            int espacio = corte.LastIndexOf(' ');
            return (espacio > 0 ? corte.Substring(0, espacio) : limpio.Substring(0, LARGO_NOMBRE)).Trim();
        }

        /// <summary>Las partidas que no llegan a la prometida (sin fecha, o que salen después).</summary>
        public static List<PartidaFechaEntregaAgenciaDTO> PartidasQueFallan(DateTime prometida, FechaEntregaAgenciaDTO calculo)
        {
            return (calculo?.Desglose ?? new List<PartidaFechaEntregaAgenciaDTO>())
                .Where(p => p != null && (!p.PuedeSalirEl.HasValue || p.PuedeSalirEl.Value.Date > prometida.Date))
                .ToList();
        }

        /// <summary>
        /// Los culpables: lo que NO está listo en Algete (tienda, de camino, proveedor, sin stock, kit) y sale después de la prometida. En
        /// «Todo junto», con la prometida pasada, hasta lo de Algete «sale después»: eso no es culpa de nadie (Carlos, 23/10/26).
        /// </summary>
        public static List<PartidaFechaEntregaAgenciaDTO> Culpables(DateTime prometida, FechaEntregaAgenciaDTO calculo) =>
            PartidasQueFallan(prometida, calculo).Where(p => !EstaListaEnAlgete(p)).ToList();

        /// <summary>El pedido tiene fecha de entrega posterior (la frase del motor «Tiene fecha de entrega el …»).</summary>
        public static bool TieneFechaEntregaPosterior(FechaEntregaAgenciaDTO calculo) => FraseDelMotor(calculo?.Motivo?.Trim(), "Tiene fecha de entrega") != null;

        /// <summary>Lo que falta ahora, con el nombre recortado (para la causa original).</summary>
        public static List<FaltaPromesaRiesgo> Faltas(DateTime prometida, FechaEntregaAgenciaDTO calculo, IDictionary<string, string> nombres) =>
            Culpables(prometida, calculo)
                .Where(p => !string.IsNullOrWhiteSpace(p.Producto))
                .GroupBy(p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new FaltaPromesaRiesgo { Producto = g.Key, Nombre = NombreCorto(Nombre(nombres, g.Key)), Unidades = g.Sum(p => p.Unidades) })
                .ToList();

        /// <summary>
        /// La frase del pedido (Carlos, 18/11/26): qué falta, cuándo llega y cuándo sale. Los productos con la misma causa y la misma fecha,
        /// juntos («y 5 productos más»). Sin culpables: la fecha de entrega del pedido (si es lo que lo retiene) o <see cref="TODO_EN_ALGETE"/>
        /// (ya no se dice «todo está listo en Algete, pero no salió el …», que no era cierto).
        /// </summary>
        /// <param name="nombres">Producto → descripción (opcional).</param>
        /// <param name="hoy">Para «hoy», «mañana» y el año.</param>
        public static string Motivo(DateTime prometida, FechaEntregaAgenciaDTO calculo, IDictionary<string, string> nombres, DateTime hoy)
        {
            List<PartidaFechaEntregaAgenciaDTO> culpables = Culpables(prometida, calculo);
            string motor = calculo?.Motivo?.Trim();
            if (!culpables.Any())
            {
                string fechaEntrega = FraseDelMotor(motor, "Tiene fecha de entrega");
                return fechaEntrega != null ? "El pedido " + char.ToLower(fechaEntrega[0], castellano) + fechaEntrega.Substring(1) : TODO_EN_ALGETE;
            }

            List<Grupo> grupos = Agrupar(culpables);
            int productos = culpables.Select(p => p.Producto?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            string falta;
            if (grupos.Count == 1)
            {
                Grupo g = grupos[0];
                bool plural = g.Unidades > 1 || g.Productos.Count > 1;
                falta = $"{(plural ? "Faltan" : "Falta")} {QueProductos(g, nombres)} ({Unidades(g.Unidades)}): {FraseLarga(g.Primera, plural, hoy)}.";
            }
            else if (productos == 1)
            {
                // Un solo producto repartido entre varios sitios: «Faltan 42485 PARCHES (3 uds.): 1 viene de camino (hoy) y 2 vienen de Reina (23/10).»
                int unidades = grupos.Sum(g => g.Unidades);
                string producto = Producto(grupos[0].Productos[0], nombres);
                falta = $"{(unidades > 1 ? "Faltan" : "Falta")} {producto} ({Unidades(unidades)}): " +
                    Enumerar(grupos.Select(g => $"{g.Unidades} {FraseCorta(g.Primera, g.Unidades > 1, hoy)}")) + ".";
            }
            else
            {
                var partes = new List<string>();
                for (int i = 0; i < grupos.Count; i++)
                {
                    Grupo g = grupos[i];
                    partes.Add(g.Productos.Count == 1
                        ? $"{g.Productos[0]} {FraseCorta(g.Primera, false, hoy)}"
                        : $"{(i > 0 ? "otros " : string.Empty)}{g.Productos.Count} {FraseCorta(g.Primera, true, hoy)}");
                }
                falta = $"Faltan {productos} productos: {Enumerar(partes)}.";
            }

            string motivo = falta + " " + Sale(culpables, grupos, hoy);
            bool hayListo = (calculo?.Desglose ?? new List<PartidaFechaEntregaAgenciaDTO>()).Any(p => p != null && EstaListaEnAlgete(p));
            if (hayListo && EsTodoJunto(motor))
            {
                motivo += " Va todo junto.";
            }
            return motivo;
        }

        /// <summary>
        /// La buena noticia de un pedido avisado al que le ha llegado lo que faltaba: «Faltaba 45949 JABON CARBON ACTIVO; llegó el 17/11.
        /// Sale hoy.» Sin la causa original, <see cref="TODO_EN_ALGETE"/>. La llegada va con la fecha (es un hecho pasado que se guarda).
        /// </summary>
        public static string YaLlego(IList<FaltaPromesaRiesgo> faltaba, DateTime llego, DateTime? sale, DateTime hoy)
        {
            List<FaltaPromesaRiesgo> lista = (faltaba ?? new List<FaltaPromesaRiesgo>()).Where(f => f != null && !string.IsNullOrWhiteSpace(f.Producto)).ToList();
            if (!lista.Any())
            {
                return TODO_EN_ALGETE;
            }
            FaltaPromesaRiesgo primera = lista[0];
            string producto = string.IsNullOrWhiteSpace(primera.Nombre) ? primera.Producto.Trim() : $"{primera.Producto.Trim()} {primera.Nombre.Trim()}";
            bool varios = lista.Count > 1;
            string mas = varios ? " " + YMas(lista.Count - 1) : string.Empty;
            string cuando = sale.HasValue ? ElDia(sale.Value, hoy) : "en el próximo picking";
            return $"{(varios ? "Faltaban" : "Faltaba")} {producto}{mas}; {(varios ? "llegaron" : "llegó")} el {Numeros(llego, hoy)}. Sale {cuando}.";
        }

        // ------------------------------------------------------------------ piezas

        private class Grupo
        {
            public PartidaFechaEntregaAgenciaDTO Primera { get; set; }
            public List<string> Productos { get; } = new List<string>();
            public int Unidades { get; set; }
            public DateTime? Orden { get; set; }
        }

        /// <summary>Misma causa (origen, tienda, pedido lejano…) y mismas fechas → un grupo. Ordenados de lo que antes sale a lo último.</summary>
        private static List<Grupo> Agrupar(IEnumerable<PartidaFechaEntregaAgenciaDTO> culpables)
        {
            var grupos = new Dictionary<string, Grupo>();
            var orden = new List<Grupo>();
            foreach (PartidaFechaEntregaAgenciaDTO p in culpables)
            {
                string clave = string.Join("|", p.Origen, p.Tienda?.Trim(), D(p.PuedeSalirEl), D(p.FechaPrevistaProveedor), p.LlegadaSupuesta.HasValue,
                    D(p.FechaOrientativa), D(p.FechaPrevistaProveedorOrientativo), p.PedidoProveedorOrientativo.HasValue, D(p.LlegaAAlgete), p.Kit?.Trim());
                if (!grupos.TryGetValue(clave, out Grupo g))
                {
                    g = new Grupo { Primera = p, Orden = p.PuedeSalirEl ?? p.FechaOrientativa ?? DateTime.MaxValue };
                    grupos[clave] = g;
                    orden.Add(g);
                }
                string producto = p.Producto?.Trim() ?? string.Empty;
                if (!g.Productos.Contains(producto, StringComparer.OrdinalIgnoreCase))
                {
                    g.Productos.Add(producto);
                }
                g.Unidades += p.Unidades;
            }
            return orden.OrderBy(g => g.Primera.PuedeSalirEl.HasValue ? 0 : 1).ThenBy(g => g.Orden).ToList();
        }

        private static string D(DateTime? fecha) => fecha?.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        /// <summary>Cuándo sale: «Sale el jueves 19/11 (aproximado).», «Lo último sale…», «Sin fecha segura (orientativo: 15/12).» o «Sin fecha.».</summary>
        private static string Sale(List<PartidaFechaEntregaAgenciaDTO> culpables, List<Grupo> grupos, DateTime hoy)
        {
            if (culpables.All(p => p.PuedeSalirEl.HasValue))
            {
                DateTime ultima = culpables.Max(p => p.PuedeSalirEl.Value.Date);
                string certeza = PeorCerteza(culpables.Where(p => p.PuedeSalirEl.Value.Date == ultima));
                bool variasFechas = culpables.Select(p => p.PuedeSalirEl.Value.Date).Distinct().Count() > 1;
                string calificativo = certeza == null ? string.Empty : $" ({certeza})";
                return $"{(variasFechas ? "Lo último sale" : "Sale")} {ElDia(ultima, hoy)}{calificativo}.";
            }
            List<PartidaFechaEntregaAgenciaDTO> sinFecha = culpables.Where(p => !p.PuedeSalirEl.HasValue).ToList();
            if (sinFecha.All(p => p.FechaOrientativa.HasValue))
            {
                DateTime orientativa = culpables.Select(p => (p.PuedeSalirEl ?? p.FechaOrientativa).Value.Date).Max();
                return $"Sin fecha segura ({TextosCertezaFechaEntrega.CORTO_ORIENTATIVO}: {Numeros(orientativa, hoy)}).";
            }
            return "Sin fecha.";
        }

        private static string PeorCerteza(IEnumerable<PartidaFechaEntregaAgenciaDTO> partidas)
        {
            string peor = partidas.Select(TextosCertezaFechaEntrega.Certeza)
                .OrderByDescending(TextosCertezaFechaEntrega.Gravedad).FirstOrDefault();
            return TextosCertezaFechaEntrega.CalificativoCorto(peor);
        }

        /// <summary>«45949 JABON CARBON ACTIVO y 5 productos más».</summary>
        private static string QueProductos(Grupo g, IDictionary<string, string> nombres)
        {
            string primero = Producto(g.Productos[0], nombres);
            return g.Productos.Count > 1 ? $"{primero} {YMas(g.Productos.Count - 1)}" : primero;
        }

        private static string YMas(int otros) => otros == 1 ? "y otro producto" : $"y {otros} productos más";

        private static string Producto(string producto, IDictionary<string, string> nombres)
        {
            string nombre = NombreCorto(Nombre(nombres, producto));
            return string.IsNullOrWhiteSpace(nombre) ? producto : $"{producto} {nombre}";
        }

        private static string Nombre(IDictionary<string, string> nombres, string producto) =>
            nombres != null && producto != null && nombres.TryGetValue(producto, out string nombre) ? nombre : null;

        /// <summary>Cuándo llega lo que falta, para una sola causa: «llegan hoy del proveedor», «viene de Reina», «el proveedor lo trae hacia el 14/12».</summary>
        internal static string FraseLarga(PartidaFechaEntregaAgenciaDTO p, bool plural, DateTime hoy)
        {
            string n = plural ? "n" : string.Empty;
            string s = plural ? "s" : string.Empty;
            switch (p.Origen)
            {
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA:
                    string tienda = CalculadoraFechaEntregaAgencia.NombreAlmacen(p.Tienda?.Trim());
                    return p.PuedeSalirEl.HasValue ? $"viene{n} de {tienda}" : $"está{n} en {tienda}, sin fecha de reposición";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO:
                    return $"viene{n} de camino desde una tienda";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR:
                    if (!p.FechaPrevistaProveedor.HasValue)
                    {
                        return $"está{n} pedido{s} al proveedor, sin fecha prevista";
                    }
                    if (p.LlegadaSupuesta.HasValue)
                    {
                        return $"el proveedor tenía que traerlo{s} el {Numeros(p.FechaPrevistaProveedor.Value, hoy)} y aún no ha llegado";
                    }
                    string dia = DiaCorto(p.FechaPrevistaProveedor.Value, hoy);
                    return dia == "hoy" || dia == "mañana" ? $"llega{n} {dia} del proveedor" : $"llega{n} del proveedor el {dia}";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK:
                    if (p.FechaPrevistaProveedorOrientativo.HasValue)
                    {
                        return $"el proveedor lo{s} trae hacia el {Numeros(p.FechaPrevistaProveedorOrientativo.Value, hoy)}";
                    }
                    return p.FechaOrientativa.HasValue ? $"aún no está{n} pedido{s} al proveedor" : "no hay stock ni pedido al proveedor";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_KIT:
                    return "hay que desmontar un kit en Algete";
                default:
                    return $"no está{n} en Algete";
            }
        }

        /// <summary>Lo mismo, corto y con la fecha entre paréntesis, para enumerar varias causas: «viene de Reina (19/11)», «del proveedor (01/12)».</summary>
        internal static string FraseCorta(PartidaFechaEntregaAgenciaDTO p, bool plural, DateTime hoy)
        {
            string n = plural ? "n" : string.Empty;
            string Con(string texto, DateTime? dia) => dia.HasValue ? $"{texto} ({DiaCorto(dia.Value, hoy)})" : texto;
            switch (p.Origen)
            {
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA:
                    string tienda = CalculadoraFechaEntregaAgencia.NombreAlmacen(p.Tienda?.Trim());
                    return p.PuedeSalirEl.HasValue ? Con($"viene{n} de {tienda}", p.LlegaAAlgete ?? p.PuedeSalirEl) : $"está{n} en {tienda}, sin fecha";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO:
                    return Con($"viene{n} de camino", p.PuedeSalirEl);
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR:
                    if (!p.FechaPrevistaProveedor.HasValue)
                    {
                        return "del proveedor, sin fecha";
                    }
                    return p.LlegadaSupuesta.HasValue ? "del proveedor, con retraso" : Con("del proveedor", p.FechaPrevistaProveedor);
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK:
                    if (p.FechaPrevistaProveedorOrientativo.HasValue)
                    {
                        return Con("del proveedor", p.FechaPrevistaProveedorOrientativo);
                    }
                    return p.FechaOrientativa.HasValue ? "sin pedir al proveedor" : "sin stock ni pedido";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_KIT:
                    return Con("de un kit", p.PuedeSalirEl);
                default:
                    return "fuera de Algete";
            }
        }

        private static string Enumerar(IEnumerable<string> partes)
        {
            List<string> lista = partes.ToList();
            return lista.Count <= 1 ? string.Join(string.Empty, lista) : string.Join(", ", lista.Take(lista.Count - 1)) + " y " + lista.Last();
        }

        private static bool EstaListaEnAlgete(PartidaFechaEntregaAgenciaDTO p)
            => p.Origen == PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE || p.Origen == PartidaFechaEntregaAgenciaDTO.ORIGEN_PICKING;

        private static bool EsTodoJunto(string motor)
            => motor != null && motor.Contains("«" + Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.TODO_JUNTO) + "»");

        /// <summary>La frase del motivo del motor que empieza por <paramref name="inicio"/>, con su punto. Null si no está.</summary>
        internal static string FraseDelMotor(string motor, string inicio)
        {
            int i = motor?.IndexOf(inicio, StringComparison.Ordinal) ?? -1;
            if (i < 0)
            {
                return null;
            }
            int fin = motor.IndexOf(". ", i, StringComparison.Ordinal);
            return fin < 0 ? motor.Substring(i).Trim() : motor.Substring(i, fin - i + 1).Trim();
        }

        /// <summary>«Reina mandó 1 de 3 en la reposición 81094». Null si la causa no dice nada de este pedido.</summary>
        public static string TextoCausa(CausaPromesaRiesgo causa, int necesita, int pedido)
        {
            if (causa == null)
            {
                return null;
            }
            if (causa.Mandadas.HasValue && !string.IsNullOrWhiteSpace(causa.Tienda))
            {
                string tienda = CalculadoraFechaEntregaAgencia.NombreAlmacen(causa.Tienda.Trim());
                string reposicion = causa.Traspaso.HasValue ? $" en la reposición {causa.Traspaso.Value}" : " en la reposición";
                return causa.Mandadas.Value < necesita
                    ? $"{tienda} mandó {causa.Mandadas.Value} de {necesita}{reposicion}"
                    : $"{tienda} mandó {causa.Mandadas.Value}{reposicion}";
            }
            if (causa.PedidoOrigen.HasValue)
            {
                return causa.PedidoOrigen.Value == pedido
                    ? "se ha modificado el pedido"
                    : $"también lo lleva el pedido {causa.PedidoOrigen.Value}, grabado o modificado después";
            }
            return string.IsNullOrWhiteSpace(causa.Texto) ? null : causa.Texto.Trim();
        }

        private static string Unidades(int unidades) => $"{unidades} {(unidades == 1 ? "ud." : "uds.")}";

        public static string TextoCliente(string cliente, string nombre)
        {
            string numero = cliente?.Trim();
            return string.IsNullOrWhiteSpace(nombre) ? numero : $"{numero} - {nombre.Trim()}";
        }

        /// <summary>«se queda sin fecha» / «se retrasa al viernes 23/10/2026».</summary>
        public static string QuePasa(DateTime? nueva) => nueva.HasValue ? $"se retrasa al {Fecha(nueva)}" : "se queda sin fecha";

        /// <summary>
        /// La campana. El título dice la fecha nueva y el cuerpo, el cliente, la prometida y la frase (que ya dice cuándo sale: no se repite).
        /// La buena noticia: «El pedido 929751 ya sale» + la frase.
        /// </summary>
        public static NotificacionPushDTO Notificacion(AvisoPromesaRiesgo aviso)
        {
            RiesgoPromesa r = aviso.Riesgo;
            string cliente = TextoCliente(aviso.Cliente, aviso.NombreCliente);
            string titulo;
            string cuerpo;
            if (aviso.YaLlego)
            {
                titulo = $"El pedido {r.Pedido} ya sale";
                cuerpo = $"Cliente {cliente}. {r.Motivo}";
            }
            else
            {
                string empeora = aviso.Empeora ? " (ya se le avisó y ha empeorado)" : string.Empty;
                titulo = $"El pedido {r.Pedido} {QuePasa(r.FechaNueva)}";
                cuerpo = $"Cliente {cliente}. Prometido a la agencia para el {Fecha(r.FechaPrometida)}{empeora}. {r.Motivo} " +
                    "Revise el pedido y, si hace falta, avise al cliente.";
            }
            return new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = cuerpo,
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["empresa"] = r.Empresa?.Trim(),
                    ["pedido"] = r.Pedido.ToString(CultureInfo.InvariantCulture),
                    ["cliente"] = aviso.Cliente?.Trim()
                }
            };
        }

        /// <summary>
        /// El correo resumen: una tabla con los pedidos en riesgo, del que antes se va a la agencia al que más tarde, y debajo, una línea por
        /// cada pedido avisado al que ya le ha llegado lo que faltaba.
        /// </summary>
        public static string CuerpoCorreo(IEnumerable<AvisoPromesaRiesgo> avisos, bool sombra)
        {
            List<AvisoPromesaRiesgo> todos = (avisos ?? Enumerable.Empty<AvisoPromesaRiesgo>()).ToList();
            List<AvisoPromesaRiesgo> lista = todos.Where(a => !a.YaLlego)
                .OrderBy(a => a.Riesgo.FechaPrometida).ThenBy(a => a.Riesgo.Pedido).ToList();
            List<AvisoPromesaRiesgo> yaSalen = todos.Where(a => a.YaLlego).OrderBy(a => a.Riesgo.Pedido).ToList();
            var s = new StringBuilder();
            if (sombra)
            {
                _ = s.Append("<p><b>MODO SOMBRA</b> (Pedidos:AvisoPromesaRiesgo = Sombra): esto NO le ha llegado a nadie más; es lo que se habría avisado.</p>");
            }
            if (lista.Any())
            {
                _ = s.Append("<p>");
                _ = s.Append(lista.Count == 1
                    ? "Este pedido ya no llega a la fecha de entrega a la agencia que se le prometió al cliente:"
                    : $"Estos {lista.Count} pedidos ya no llegan a la fecha de entrega a la agencia que se le prometió al cliente:");
                _ = s.Append("</p>");
                _ = s.Append("<table border=\"1\" cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse; font-family:Arial; font-size:12px\">");
                _ = s.Append("<tr style=\"background-color:#eeeeee\"><th>Pedido</th><th>Cliente</th><th>Comercial</th><th>Prometida</th><th>Ahora</th><th>Qué falta</th></tr>");
                foreach (AvisoPromesaRiesgo a in lista)
                {
                    RiesgoPromesa r = a.Riesgo;
                    string estilo = r.FechaNueva.HasValue ? string.Empty : " style=\"color:red\"";
                    _ = s.Append("<tr>")
                        .Append($"<td>{r.Pedido}</td>")
                        .Append($"<td>{Html(TextoCliente(a.Cliente, a.NombreCliente))}</td>")
                        .Append($"<td>{Html(a.Comerciales)}</td>")
                        .Append($"<td>{Html(Fecha(r.FechaPrometida))}</td>")
                        .Append($"<td{estilo}><b>{Html(Fecha(r.FechaNueva))}</b>{(a.Empeora ? " (ha empeorado)" : string.Empty)}</td>")
                        .Append($"<td>{Html(r.Motivo)}</td>")
                        .Append("</tr>");
                }
                _ = s.Append("</table>");
                _ = s.Append("<p>Revise cada pedido y, si hace falta, avise al cliente o cambie lo que convenga (modo de servir, reposición puntual, " +
                    "sustituir el producto o renegociar la fecha). No se le volverá a avisar de estos pedidos salvo que empeoren o les llegue lo que falta.</p>");
            }
            if (yaSalen.Any())
            {
                _ = s.Append(yaSalen.Count == 1
                    ? "<p>Ya ha llegado lo que faltaba a este pedido del que se le avisó:</p><ul>"
                    : $"<p>Ya ha llegado lo que faltaba a estos {yaSalen.Count} pedidos de los que se le avisó:</p><ul>");
                foreach (AvisoPromesaRiesgo a in yaSalen)
                {
                    _ = s.Append($"<li>Pedido {a.Riesgo.Pedido} ({Html(TextoCliente(a.Cliente, a.NombreCliente))}): {Html(a.Riesgo.Motivo)}</li>");
                }
                _ = s.Append("</ul>");
            }
            return s.ToString();
        }

        public static string AsuntoCorreo(int pedidos, bool sombra) =>
            (sombra ? "[Sombra] " : string.Empty) + (pedidos == 1
                ? "Un pedido ya no llega a la fecha prometida"
                : $"{pedidos} pedidos ya no llegan a la fecha prometida");

        /// <summary>El asunto con los dos bloques: los que no llegan y, aparte, los que ya salen.</summary>
        public static string AsuntoCorreo(IEnumerable<AvisoPromesaRiesgo> avisos, bool sombra)
        {
            List<AvisoPromesaRiesgo> lista = (avisos ?? Enumerable.Empty<AvisoPromesaRiesgo>()).ToList();
            int riesgos = lista.Count(a => !a.YaLlego);
            int yaSalen = lista.Count(a => a.YaLlego);
            if (riesgos > 0)
            {
                return AsuntoCorreo(riesgos, sombra) + (yaSalen == 0 ? string.Empty : yaSalen == 1 ? " (y uno ya sale)" : $" (y {yaSalen} ya salen)");
            }
            return (sombra ? "[Sombra] " : string.Empty) + (yaSalen == 1
                ? "Ya sale un pedido que no llegaba a la fecha prometida"
                : $"Ya salen {yaSalen} pedidos que no llegaban a la fecha prometida");
        }

        /// <summary>Solo &amp; &lt; &gt; y comillas: las tildes se quedan como están (como GestorPresupuestos).</summary>
        private static string Html(string texto) => (texto ?? string.Empty)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
