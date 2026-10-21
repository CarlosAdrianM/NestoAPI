using ModeloSugerenciasProductos.Datos;
using ModeloSugerenciasProductos.Entrenamiento;
using ModeloSugerenciasProductos.Reglas;

namespace ModeloSugerenciasProductos.Features;

/// <summary>
/// Las fechas del entrenamiento. Instantáneas el día 1 de cada mes:
/// <list type="bullet">
/// <item>la última de validación es el mes cuyo pedido más tardío ya tiene cerrados sus 30 días de etiqueta antes de hoy;</item>
/// <item>validación: los <c>MesesValidacion</c> meses que terminan en ella (<see cref="Corte"/> = la primera);</item>
/// <item>un mes de hueco (sus etiquetas pisarían la validación);</item>
/// <item>entrenamiento: las <c>Instantaneas</c> anteriores, sin empezar antes de <c>MesesHistoriaMinima</c> meses de datos.</item>
/// </list>
/// </summary>
public sealed class VentanaEntrenamiento
{
    public DateTime Hoy { get; private set; }
    public DateTime DatosDesde { get; private set; }
    /// <summary>Fin (excluido) de lo leído: hoy.</summary>
    public DateTime DatosHasta { get; private set; }
    public List<DateTime> InstantaneasEntrenamiento { get; } = new();
    public List<DateTime> InstantaneasValidacion { get; } = new();
    public DateTime Corte { get; private set; }

    public static DateTime MesDe(DateTime fecha) => new(fecha.Year, fecha.Month, 1);

    public static VentanaEntrenamiento Calcular(DateTime hoy, OpcionesEntrenamiento op)
    {
        var v = new VentanaEntrenamiento { Hoy = hoy.Date, DatosHasta = hoy.Date, DatosDesde = MesDe(hoy.Date.AddMonths(-op.Meses)) };
        DateTime ultima = MesDe(hoy.Date.AddDays(-op.DiasEtiqueta)).AddMonths(-1);
        for (int i = op.MesesValidacion - 1; i >= 0; i--) v.InstantaneasValidacion.Add(ultima.AddMonths(-i));
        v.Corte = v.InstantaneasValidacion.Count > 0 ? v.InstantaneasValidacion[0] : ultima.AddMonths(1);
        DateTime ultimaEntrenamiento = v.Corte.AddMonths(-2);
        DateTime primeraPosible = v.DatosDesde.AddMonths(op.MesesHistoriaMinima);
        for (int i = op.Instantaneas - 1; i >= 0; i--)
        {
            DateTime d = ultimaEntrenamiento.AddMonths(-i);
            if (d >= primeraPosible) v.InstantaneasEntrenamiento.Add(d);
        }
        v.InstantaneasValidacion.RemoveAll(d => d < primeraPosible);
        return v;
    }
}

/// <summary>Un cliente que hace un pedido (el primero del mes de la instantánea), con sus candidatos.</summary>
public sealed class Sesion
{
    public string Id { get; set; } = "";
    public string Cliente { get; set; } = "";
    public DateTime Dia { get; set; }
    public DateTime Instantanea { get; set; }
    public bool Validacion { get; set; }
    public List<string> Carrito { get; set; } = new();
    public List<FilaEntrenamiento> Filas { get; set; } = new();
    /// <summary>Todo lo que se llevó (sugerible y fuera del carrito), esté o no entre los candidatos.</summary>
    public HashSet<string> Positivos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Reglas1 { get; set; } = new();
    public List<string> Reglas1Orden { get; set; } = new();
    public Dictionary<string, int> ComprasPorProducto { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>NestoAPI#683: grupo del estado del cliente el día del pedido (<see cref="EstadosCliente.Grupo"/>).</summary>
    public string EstadoCliente { get; set; } = EstadosCliente.NADA;
}

public sealed class Dataset
{
    public VentanaEntrenamiento Ventana { get; set; } = null!;
    public List<Sesion> Entrenamiento { get; } = new();
    public List<Sesion> Validacion { get; } = new();
    public IEnumerable<Sesion> Todas => Entrenamiento.Concat(Validacion);
}

/// <summary>
/// NestoAPI#671 (corte 2b): las instantáneas del entrenamiento, sin fuga de datos. Por cada instantánea d (día 1 de un mes):
/// <list type="number">
/// <item>las tablas del job como habrían estado el día d (<see cref="ContextoInstantanea.Calcular"/>: compras de los 24 meses anteriores);</item>
/// <item>por cliente, su primer pedido del mes (cliente y día de albarán, productos sugeribles);</item>
/// <item>carrito simulado: la mitad de las líneas del pedido, al azar con semilla fija por cliente y día; en el 30 % de las
/// sesiones, el carrito vacío;</item>
/// <item>historial del cliente: sus compras de los 24 meses anteriores al día del pedido;</item>
/// <item>etiqueta: el producto está en la otra mitad del pedido o el cliente se lo lleva en los 30 días siguientes (sin pasar,
/// en el entrenamiento, del corte de la validación).</item>
/// <item>NestoAPI#683: estado del cliente el día del pedido y popularidad por estado con el estado de cada cliente el día d
/// (<see cref="HistorialEstadosCliente"/>: el de hoy deshaciendo los cambios posteriores).</item>
/// </list>
/// Determinista: misma entrada y misma semilla, mismo dataset (hash FNV propio: string.GetHashCode cambia en cada proceso).
/// </summary>
public static class ConstructorInstantaneas
{
    public static Dataset Construir(DatosCrudos datos, VentanaEntrenamiento ventana, OpcionesEntrenamiento op, Action<string>? log = null)
    {
        var dataset = new Dataset { Ventana = ventana };
        HistorialEstadosCliente estados = datos.Estados();
        var sugeribles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        FichaProducto? Ficha(string p) => datos.Fichas.TryGetValue(p, out FichaProducto? f) ? f : null;
        bool EsSugerible(string p)
        {
            if (!sugeribles.TryGetValue(p, out bool vale))
            {
                vale = ReglasSugerencias.EsSugerible(Ficha(p));
                sugeribles[p] = vale;
            }
            return vale;
        }

        List<Compra> compras = datos.Compras
            .Where(c => !string.IsNullOrWhiteSpace(c.Cliente) && !string.IsNullOrWhiteSpace(c.Producto)
                && c.Dia >= ventana.DatosDesde && c.Dia < ventana.DatosHasta)
            .Select(c => new Compra { Cliente = c.Cliente.Trim(), Producto = c.Producto.Trim(), Dia = c.Dia.Date, BaseImponible = c.BaseImponible, Cantidad = c.Cantidad })
            .OrderBy(c => c.Dia).ThenBy(c => c.Cliente, StringComparer.Ordinal).ThenBy(c => c.Producto, StringComparer.Ordinal)
            .ToList();
        Dictionary<string, List<Compra>> porCliente = compras.GroupBy(c => c.Cliente, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        HashSet<string>? muestra = null;
        if (op.MuestraClientes > 0)
        {
            muestra = new HashSet<string>(porCliente.Keys
                .OrderBy(c => Hash(c.ToUpperInvariant() + "|" + op.Semilla))
                .ThenBy(c => c, StringComparer.Ordinal)
                .Take(op.MuestraClientes), StringComparer.OrdinalIgnoreCase);
        }

        foreach ((DateTime d, bool validacion) in ventana.InstantaneasEntrenamiento.Select(d => (d, false))
            .Concat(ventana.InstantaneasValidacion.Select(d => (d, true))))
        {
            DateTime fechaContexto = d;
            ContextoInstantanea contexto = ContextoInstantanea.Calcular(compras, Ficha, d, c => estados.EstadoEn(c, fechaContexto));
            DateTime finMes = d.AddMonths(1);
            DateTime limiteEtiquetas = validacion ? ventana.DatosHasta : ventana.Corte;
            int sesiones = 0;
            foreach (KeyValuePair<string, List<Compra>> cliente in porCliente.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                if (muestra != null && !muestra.Contains(cliente.Key)) continue;
                List<Compra> suyas = cliente.Value;
                Compra? primera = suyas.FirstOrDefault(c => c.Dia >= d && c.Dia < finMes && EsSugerible(c.Producto));
                if (primera == null) continue;
                DateTime dia = primera.Dia;
                List<string> cesta = suyas.Where(c => c.Dia == dia && EsSugerible(c.Producto))
                    .Select(c => c.Producto).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.Ordinal).ToList();

                var azar = new Random(Hash(cliente.Key.ToUpperInvariant() + "|" + dia.ToString("yyyyMMdd") + "|" + op.Semilla));
                bool vacio = azar.NextDouble() < op.ProporcionCarritoVacio;
                List<string> carrito = vacio ? new List<string>()
                    : cesta.OrderBy(_ => azar.Next()).Take(cesta.Count / 2).OrderBy(p => p, StringComparer.Ordinal).ToList();
                var enCarrito = new HashSet<string>(carrito, StringComparer.OrdinalIgnoreCase);

                DateTime finEtiqueta = dia.AddDays(op.DiasEtiqueta + 1);
                if (finEtiqueta > limiteEtiquetas) finEtiqueta = limiteEtiquetas;
                var positivos = new HashSet<string>(cesta.Where(p => !enCarrito.Contains(p)), StringComparer.OrdinalIgnoreCase);
                foreach (Compra c in suyas.Where(c => c.Dia > dia && c.Dia < finEtiqueta && EsSugerible(c.Producto) && !enCarrito.Contains(c.Producto)))
                {
                    positivos.Add(c.Producto);
                }

                short? estado = estados.EstadoEn(cliente.Key, dia);
                ResultadoCandidatos candidatos = CalculadoraFeatures.Calcular(contexto, cliente.Key, suyas.Where(c => c.Dia < dia),
                    enCarrito, Ficha, dia, op.MaximoCandidatosPorFuente, estadoCliente: estado);
                string id = cliente.Key + "|" + dia.ToString("yyyyMMdd");
                foreach (FilaEntrenamiento fila in candidatos.Filas)
                {
                    fila.Sesion = id;
                    fila.Label = positivos.Contains(fila.Producto);
                    fila.Relevancia = fila.Label ? 1f : 0f;
                }
                var sesion = new Sesion
                {
                    Id = id,
                    Cliente = cliente.Key,
                    Dia = dia,
                    Instantanea = d,
                    Validacion = validacion,
                    Carrito = carrito,
                    Filas = candidatos.Filas,
                    Positivos = positivos,
                    Reglas1 = candidatos.Reglas1,
                    Reglas1Orden = candidatos.Reglas1Orden,
                    ComprasPorProducto = candidatos.ComprasPorProducto,
                    EstadoCliente = EstadosCliente.Grupo(estado)
                };
                (validacion ? dataset.Validacion : dataset.Entrenamiento).Add(sesion);
                sesiones++;
            }
            log?.Invoke($"  Instantánea {d:MM/yyyy}{(validacion ? " (validación)" : "")}: {sesiones} sesiones.");
        }
        return dataset;
    }

    /// <summary>FNV-1a de 32 bits (estable entre procesos y versiones de .NET).</summary>
    public static int Hash(string texto)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in texto)
            {
                h ^= c;
                h *= 16777619;
            }
            return (int)(h & 0x7FFFFFFF);
        }
    }
}
