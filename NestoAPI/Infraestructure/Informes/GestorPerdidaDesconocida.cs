using NestoAPI.Models;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// NestoAPI#893 (Carlos 24/11/26, tras el inventario del 21/11): pérdida desconocida de cada almacén (ALG, REI, ALC), mes a mes y
    /// acumulada desde el 1 de enero, a COSTE (Productos.PrecioMedio de hoy, el mismo criterio que IndicadoresStock): solo Dirección.
    /// Se parte de ExtractoProducto (empresas 1 y 3), no de la tabla Inventarios: cuentan el inventario («_RegInvent») y los «No está» /
    /// diferencias de reposición («_ErrRepo»), como en #886. Por producto, almacén y periodo se neta lo que entra y sale: lo que queda
    /// en negativo FALTA y lo que queda en positivo SOBRA. Lo que falta en un almacén y sobra en otro (mismo producto, mismo periodo)
    /// CUADRA; lo que no cuadra con nada es la pérdida de verdad. LA REGLA (pura) es <see cref="Construir"/>; una sola consulta agregada.
    /// <para>Los demás diarios de producto a mano (mermas, «_Santi»…) salen aparte en <c>DiariosSinClasificar</c> y NO cuentan hasta que
    /// se decida cuáles son pérdida desconocida y cuáles tienen causa (rotura, caducado, muestra, regalo, componente sacado…).</para>
    /// <para>«En tránsito» (#906): columna propia sin imputar a ningún almacén, hoy vacía (<see cref="DIARIOS_EN_TRANSITO"/>).</para>
    /// </summary>
    public static class GestorPerdidaDesconocida
    {
        public const string DIARIO_INVENTARIO = GestorAltasBajasTienda.DIARIO_INVENTARIO;
        public const string DIARIO_ERROR_REPOSICION = GestorAltasBajasTienda.DIARIO_ERROR_REPOSICION;

        /// <summary>Diarios de sistema que nunca son pérdida (montar/desmontar kits, traspasos a almacenes ficticios de #894).</summary>
        public static readonly string[] DIARIOS_SISTEMA = { "_MontarKit", "_Ficticio" };

        /// <summary>
        /// Los diarios que serán la pérdida «en tránsito» de #906 (resolución «pérdida en tránsito» con las dos firmas). HOY NINGUNO: un
        /// desacuerdo entre origen y destino acaba en «_ErrRepo» en uno de los dos almacenes, igual que cualquier otra diferencia, y no
        /// hay forma de distinguirlo. Cuando #906 escriba su movimiento con un diario propio, basta con ponerlo aquí.
        /// </summary>
        public static readonly string[] DIARIOS_EN_TRANSITO = new string[0];

        /// <summary>Los almacenes del informe, en este orden (también el del cuadre).</summary>
        public static readonly string[] ALMACENES = { Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        public enum Categoria { Desconocida, EnTransito, SinClasificar, Ninguna }

        // @p0 empresa (y la de la ficha), @p1 espejo, @p2 desde (incluido), @p3 hasta (excluido), @p4..@p6 almacenes, @p7 inventario,
        // @p8 errores de reposición, @p9..@p10 diarios de sistema. Solo lectura, NOLOCK y agregada por almacén, mes, producto y diario.
        // Los diarios que no son inventario ni reposición solo entran si son «a mano»: sin cliente, proveedor, pedido, albarán ni traspaso.
        internal const string SQL_PERDIDA_DESCONOCIDA = @"
SELECT RTRIM(e.[Almacén]) AS Almacen, MONTH(e.Fecha) AS Mes, RTRIM(e.[Número]) AS Producto, RTRIM(e.Diario) AS Diario,
       CAST(SUM(CASE WHEN e.Cantidad > 0 THEN e.Cantidad ELSE 0 END) AS int) AS Altas,
       CAST(-SUM(CASE WHEN e.Cantidad < 0 THEN e.Cantidad ELSE 0 END) AS int) AS Bajas,
       MAX(RTRIM(p.Nombre)) AS Nombre, MAX(RTRIM(p.Familia)) AS Familia,
       MAX(CAST(ISNULL(p.PrecioMedio, 0) AS decimal(19, 4))) AS PrecioMedio
FROM ExtractoProducto e WITH (NOLOCK)
     LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = @p0 AND p.[Número] = e.[Número]
WHERE e.Empresa IN (@p0, @p1) AND e.[Almacén] IN (@p4, @p5, @p6) AND e.Fecha >= @p2 AND e.Fecha < @p3 AND e.Cantidad <> 0
      AND e.Diario IS NOT NULL
      AND (e.Diario IN (@p7, @p8)
           OR (e.Diario NOT IN (@p9, @p10)
               AND ISNULL(RTRIM(e.[Nº Cliente]), '') = '' AND ISNULL(RTRIM(e.NºProveedor), '') = ''
               AND ISNULL(e.NºTraspaso, 0) = 0 AND ISNULL(e.Albarán, 0) = 0 AND ISNULL(e.NºPedido, 0) = 0
               AND ISNULL(e.LinPedido, 0) = 0))
GROUP BY e.[Almacén], MONTH(e.Fecha), e.[Número], e.Diario";

        internal static object[] Parametros(DateTime desde, DateTime hasta) => new object[]
        {
            new SqlParameter("@p0", Constantes.Empresas.EMPRESA_POR_DEFECTO),
            new SqlParameter("@p1", Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO),
            new SqlParameter("@p2", desde.Date),
            new SqlParameter("@p3", hasta.Date.AddDays(1)),
            new SqlParameter("@p4", ALMACENES[0]),
            new SqlParameter("@p5", ALMACENES[1]),
            new SqlParameter("@p6", ALMACENES[2]),
            new SqlParameter("@p7", DIARIO_INVENTARIO),
            new SqlParameter("@p8", DIARIO_ERROR_REPOSICION),
            new SqlParameter("@p9", DIARIOS_SISTEMA[0]),
            new SqlParameter("@p10", DIARIOS_SISTEMA[1])
        };

        /// <summary>
        /// El periodo: del 1 de enero del año a «hasta». Sin nada, este año hasta hoy; con año pasado, hasta el 31/12; con «hasta», su
        /// año. Devuelve el error, o null si vale.
        /// </summary>
        public static string ResolverPeriodo(int? anio, DateTime? hasta, DateTime hoy, out DateTime desde, out DateTime fechaHasta)
        {
            desde = fechaHasta = default;
            DateTime fin;
            if (hasta.HasValue)
            {
                fin = hasta.Value.Date;
                if (anio.HasValue && anio.Value != fin.Year)
                {
                    return "«hasta» tiene que ser del mismo año que «anio».";
                }
                if (fin > hoy.Date)
                {
                    return "«hasta» no puede ser posterior a hoy.";
                }
            }
            else
            {
                int elAnio = anio ?? hoy.Year;
                if (elAnio > hoy.Year)
                {
                    return $"El año {elAnio} aún no ha empezado.";
                }
                if (elAnio < 2000)
                {
                    return "El año tiene que ser de 2000 en adelante.";
                }
                fin = elAnio < hoy.Year ? new DateTime(elAnio, 12, 31) : hoy.Date;
            }
            if (fin.Year < 2000)
            {
                return "El año tiene que ser de 2000 en adelante.";
            }
            desde = new DateTime(fin.Year, 1, 1);
            fechaHasta = fin;
            return null;
        }

        public static Categoria CategoriaDe(string diario)
        {
            string d = diario?.Trim();
            if (string.IsNullOrEmpty(d) || DIARIOS_SISTEMA.Any(s => string.Equals(s, d, StringComparison.OrdinalIgnoreCase)))
            {
                return Categoria.Ninguna;
            }
            if (DIARIOS_EN_TRANSITO.Any(s => string.Equals(s, d, StringComparison.OrdinalIgnoreCase)))
            {
                return Categoria.EnTransito;
            }
            if (string.Equals(d, DIARIO_INVENTARIO, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d, DIARIO_ERROR_REPOSICION, StringComparison.OrdinalIgnoreCase))
            {
                return Categoria.Desconocida;
            }
            return Categoria.SinClasificar;
        }

        /// <summary>
        /// El cuadre de UN producto en UN periodo: lo que falta en unos almacenes se compensa con lo que sobra en otros, en el orden de
        /// <see cref="ALMACENES"/> (y después los demás por orden alfabético). Devuelve, por almacén, las unidades cuadradas (en positivo)
        /// y con quién.
        /// </summary>
        public static Dictionary<string, (int Unidades, List<string> Con)> Cuadrar(IDictionary<string, int> netoPorAlmacen)
        {
            var resultado = new Dictionary<string, (int Unidades, List<string> Con)>(StringComparer.OrdinalIgnoreCase);
            if (netoPorAlmacen == null)
            {
                return resultado;
            }
            List<string> orden = netoPorAlmacen.Keys
                .OrderBy(OrdenAlmacen)
                .ThenBy(a => a, StringComparer.Ordinal)
                .ToList();
            Dictionary<string, int> faltan = orden.Where(a => netoPorAlmacen[a] < 0).ToDictionary(a => a, a => -netoPorAlmacen[a]);
            Dictionary<string, int> sobran = orden.Where(a => netoPorAlmacen[a] > 0).ToDictionary(a => a, a => netoPorAlmacen[a]);
            foreach (string f in faltan.Keys.ToList())
            {
                foreach (string s in sobran.Keys.ToList())
                {
                    int cuanto = Math.Min(faltan[f], sobran[s]);
                    if (cuanto <= 0)
                    {
                        continue;
                    }
                    faltan[f] -= cuanto;
                    sobran[s] -= cuanto;
                    Sumar(resultado, f, cuanto, s);
                    Sumar(resultado, s, cuanto, f);
                }
            }
            return resultado;
        }

        private static int OrdenAlmacen(string almacen)
        {
            int i = Array.IndexOf(ALMACENES, almacen?.Trim().ToUpperInvariant());
            return i >= 0 ? i : ALMACENES.Length;
        }

        private static void Sumar(Dictionary<string, (int Unidades, List<string> Con)> r, string almacen, int unidades, string con)
        {
            if (!r.TryGetValue(almacen, out (int Unidades, List<string> Con) actual))
            {
                actual = (0, new List<string>());
            }
            if (!actual.Con.Contains(con))
            {
                actual.Con.Add(con);
            }
            r[almacen] = (actual.Unidades + unidades, actual.Con);
        }

        private class Fila
        {
            public string Almacen;
            public int Mes;
            public string Producto;
            public string Diario;
            public int Altas;
            public int Bajas;
            public Categoria Categoria;
        }

        private class Ficha
        {
            public string Nombre;
            public string Familia;
            public decimal PrecioMedio;
        }

        /// <summary>LA REGLA: el informe a partir de las filas agregadas, del 1 de enero de <paramref name="hasta"/> a <paramref name="hasta"/>.</summary>
        public static PerdidaDesconocidaDTO Construir(IEnumerable<FilaPerdidaDesconocidaDTO> filas, DateTime hasta)
        {
            int anio = hasta.Year;
            int ultimoMes = hasta.Month;
            List<FilaPerdidaDesconocidaDTO> crudas = (filas ?? Enumerable.Empty<FilaPerdidaDesconocidaDTO>())
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Producto) && (f.Altas != 0 || f.Bajas != 0))
                .ToList();

            var fichas = new Dictionary<string, Ficha>(StringComparer.OrdinalIgnoreCase);
            foreach (FilaPerdidaDesconocidaDTO f in crudas)
            {
                string p = f.Producto.Trim();
                if (!fichas.TryGetValue(p, out Ficha ficha))
                {
                    ficha = new Ficha();
                    fichas[p] = ficha;
                }
                ficha.Nombre = ficha.Nombre ?? VacioANull(f.Nombre);
                ficha.Familia = ficha.Familia ?? VacioANull(f.Familia);
                if (ficha.PrecioMedio == 0 && (f.PrecioMedio ?? 0) != 0)
                {
                    ficha.PrecioMedio = f.PrecioMedio.Value;
                }
            }

            List<Fila> lineas = crudas
                .Select(f => new Fila
                {
                    Almacen = (f.Almacen ?? string.Empty).Trim().ToUpperInvariant(),
                    Mes = f.Mes,
                    Producto = f.Producto.Trim(),
                    Diario = f.Diario?.Trim(),
                    Altas = Math.Max(0, f.Altas),
                    Bajas = Math.Max(0, f.Bajas),
                    Categoria = CategoriaDe(f.Diario)
                })
                .Where(f => f.Mes >= 1 && f.Mes <= ultimoMes)
                .ToList();

            decimal Valor(string producto, int unidades) =>
                Redondear(unidades * (fichas.TryGetValue(producto, out Ficha fi) ? fi.PrecioMedio : 0m));

            List<Fila> desconocida = lineas.Where(l => l.Categoria == Categoria.Desconocida && ALMACENES.Contains(l.Almacen)).ToList();
            List<Fila> enTransito = lineas.Where(l => l.Categoria == Categoria.EnTransito).ToList();

            var informe = new PerdidaDesconocidaDTO
            {
                Anio = anio,
                Desde = new DateTime(anio, 1, 1),
                Hasta = hasta.Date,
                DiariosQueCuentan = new List<string> { DIARIO_INVENTARIO, DIARIO_ERROR_REPOSICION }
            };

            // Periodos: 1..último mes y el acumulado (0).
            var porAlmacen = ALMACENES.ToDictionary(a => a, a => new AlmacenPerdidaDesconocidaDTO { Almacen = a });
            foreach (int mes in Enumerable.Range(1, ultimoMes).Concat(new[] { 0 }))
            {
                List<Fila> delPeriodo = mes == 0 ? desconocida : desconocida.Where(l => l.Mes == mes).ToList();
                Dictionary<string, PeriodoPerdidaDesconocidaDTO> periodos = Periodo(delPeriodo, mes, Valor);
                foreach (string a in ALMACENES)
                {
                    if (mes == 0)
                    {
                        porAlmacen[a].Acumulado = periodos[a];
                    }
                    else
                    {
                        porAlmacen[a].Meses.Add(periodos[a]);
                    }
                }
                PeriodoPerdidaDesconocidaDTO transito = PeriodoSinCuadre(mes == 0 ? enTransito : enTransito.Where(l => l.Mes == mes).ToList(), mes, Valor);
                if (mes == 0)
                {
                    informe.EnTransitoAcumulado = transito;
                }
                else
                {
                    informe.EnTransito.Add(transito);
                }
            }
            informe.Almacenes = ALMACENES.Select(a => porAlmacen[a]).ToList();
            informe.Total = Sumar(informe.Almacenes.Select(a => a.Acumulado).Concat(new[] { informe.EnTransitoAcumulado }));

            // Productos del acumulado, por almacén.
            Dictionary<string, Dictionary<string, (int Unidades, List<string> Con)>> cuadreAnual = desconocida
                .GroupBy(l => l.Producto, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Cuadrar(g.GroupBy(l => l.Almacen).ToDictionary(x => x.Key, x => x.Sum(l => l.Altas - l.Bajas))),
                    StringComparer.OrdinalIgnoreCase);
            informe.Productos = desconocida
                .GroupBy(l => new { l.Almacen, l.Producto })
                .Select(g =>
                {
                    int alta = g.Sum(l => l.Altas);
                    int baja = g.Sum(l => l.Bajas);
                    int neto = alta - baja;
                    cuadreAnual[g.Key.Producto].TryGetValue(g.Key.Almacen, out (int Unidades, List<string> Con) c);
                    int cuadradas = c.Unidades;
                    int sinCuadrar = neto < 0 ? neto + cuadradas : neto - cuadradas;
                    fichas.TryGetValue(g.Key.Producto, out Ficha ficha);
                    return new ProductoPerdidaDesconocidaDTO
                    {
                        Almacen = g.Key.Almacen,
                        Producto = g.Key.Producto,
                        Nombre = ficha?.Nombre,
                        Familia = ficha?.Familia,
                        PrecioMedio = ficha?.PrecioMedio ?? 0m,
                        UnidadesAlta = alta,
                        UnidadesBaja = baja,
                        UnidadesNeto = neto,
                        ValorNeto = Valor(g.Key.Producto, neto),
                        UnidadesCuadradas = cuadradas,
                        CuadraCon = c.Con ?? new List<string>(),
                        UnidadesSinCuadrar = sinCuadrar,
                        ValorSinCuadrar = Valor(g.Key.Producto, sinCuadrar),
                        ApareceYDesaparece = alta > 0 && baja > 0
                    };
                })
                .Where(p => p.UnidadesNeto != 0 || p.ApareceYDesaparece)
                .OrderBy(p => p.ValorSinCuadrar)
                .ThenBy(p => Array.IndexOf(ALMACENES, p.Almacen))
                .ThenBy(p => p.Producto, StringComparer.Ordinal)
                .ToList();

            informe.DiariosSinClasificar = lineas
                .Where(l => l.Categoria == Categoria.SinClasificar)
                .GroupBy(l => new { l.Almacen, Diario = l.Diario ?? string.Empty })
                .Select(g => new DiarioSinClasificarDTO
                {
                    Almacen = g.Key.Almacen,
                    Diario = g.Key.Diario,
                    UnidadesAlta = g.Sum(l => l.Altas),
                    UnidadesBaja = g.Sum(l => l.Bajas),
                    UnidadesNeto = g.Sum(l => l.Altas - l.Bajas),
                    ValorNeto = g.GroupBy(l => l.Producto).Sum(p => Valor(p.Key, p.Sum(l => l.Altas - l.Bajas))),
                    NumeroProductos = g.Select(l => l.Producto).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                })
                .OrderBy(d => Array.IndexOf(ALMACENES, d.Almacen))
                .ThenBy(d => d.Diario, StringComparer.Ordinal)
                .ToList();

            informe.Notas = new List<string>
            {
                "Valorado a coste con el precio medio de HOY de cada producto (también los meses pasados).",
                $"Cuentan el inventario ({DIARIO_INVENTARIO}) y los «No está» y diferencias de reposición ({DIARIO_ERROR_REPOSICION}), empresas 1 y 3.",
                "Cada mes se neta producto a producto: lo que queda en negativo falta y lo que queda en positivo sobra. El acumulado se neta en el año entero.",
                "Cuadra lo que falta en un almacén y sobra en otro del mismo producto en el mismo periodo (el mes o el año). Lo que no cuadra es la pérdida de verdad.",
                "Aún no se cuadra contra referencias parecidas del mismo almacén (caso 44411/44412).",
                "Los diarios de producto a mano que no son inventario ni reposición salen en «Diarios sin clasificar» y no cuentan.",
                "«En tránsito» (#906) va sin imputar a ningún almacén; hoy no hay forma de distinguirlo y sale a cero."
            };
            return informe;
        }

        /// <summary>Un periodo de los tres almacenes, con el cuadre producto a producto.</summary>
        private static Dictionary<string, PeriodoPerdidaDesconocidaDTO> Periodo(List<Fila> filas, int mes, Func<string, int, decimal> valor)
        {
            var r = ALMACENES.ToDictionary(a => a, a => new PeriodoPerdidaDesconocidaDTO { Mes = mes });
            foreach (IGrouping<string, Fila> producto in filas.GroupBy(l => l.Producto, StringComparer.OrdinalIgnoreCase))
            {
                Dictionary<string, int> netos = producto.GroupBy(l => l.Almacen).ToDictionary(g => g.Key, g => g.Sum(l => l.Altas - l.Bajas));
                Dictionary<string, (int Unidades, List<string> Con)> cuadre = Cuadrar(netos);
                foreach (KeyValuePair<string, int> n in netos)
                {
                    if (!r.TryGetValue(n.Key, out PeriodoPerdidaDesconocidaDTO p) || n.Value == 0)
                    {
                        continue;
                    }
                    int cuadradas = cuadre.TryGetValue(n.Key, out (int Unidades, List<string> Con) c) ? c.Unidades : 0;
                    if (n.Value < 0)
                    {
                        p.FaltaUnidades += -n.Value;
                        p.FaltaValor += valor(producto.Key, -n.Value);
                        p.FaltaCuadradaUnidades += cuadradas;
                        p.FaltaCuadradaValor += valor(producto.Key, cuadradas);
                    }
                    else
                    {
                        p.SobraUnidades += n.Value;
                        p.SobraValor += valor(producto.Key, n.Value);
                        p.SobraCuadradaUnidades += cuadradas;
                        p.SobraCuadradaValor += valor(producto.Key, cuadradas);
                    }
                }
            }
            foreach (PeriodoPerdidaDesconocidaDTO p in r.Values)
            {
                Completar(p);
            }
            return r;
        }

        /// <summary>Un periodo sin cuadre entre almacenes (la pérdida en tránsito no es de ninguno).</summary>
        private static PeriodoPerdidaDesconocidaDTO PeriodoSinCuadre(List<Fila> filas, int mes, Func<string, int, decimal> valor)
        {
            var p = new PeriodoPerdidaDesconocidaDTO { Mes = mes };
            foreach (IGrouping<string, Fila> producto in filas.GroupBy(l => l.Producto, StringComparer.OrdinalIgnoreCase))
            {
                int neto = producto.Sum(l => l.Altas - l.Bajas);
                if (neto < 0)
                {
                    p.FaltaUnidades += -neto;
                    p.FaltaValor += valor(producto.Key, -neto);
                }
                else if (neto > 0)
                {
                    p.SobraUnidades += neto;
                    p.SobraValor += valor(producto.Key, neto);
                }
            }
            Completar(p);
            return p;
        }

        private static void Completar(PeriodoPerdidaDesconocidaDTO p)
        {
            p.PerdidaRealUnidades = p.FaltaUnidades - p.FaltaCuadradaUnidades;
            p.PerdidaRealValor = p.FaltaValor - p.FaltaCuadradaValor;
            p.SobraRealUnidades = p.SobraUnidades - p.SobraCuadradaUnidades;
            p.SobraRealValor = p.SobraValor - p.SobraCuadradaValor;
            p.NetoUnidades = p.SobraUnidades - p.FaltaUnidades;
            p.NetoValor = p.SobraValor - p.FaltaValor;
        }

        private static PeriodoPerdidaDesconocidaDTO Sumar(IEnumerable<PeriodoPerdidaDesconocidaDTO> periodos)
        {
            var t = new PeriodoPerdidaDesconocidaDTO { Mes = 0 };
            foreach (PeriodoPerdidaDesconocidaDTO p in periodos.Where(x => x != null))
            {
                t.FaltaUnidades += p.FaltaUnidades;
                t.FaltaValor += p.FaltaValor;
                t.SobraUnidades += p.SobraUnidades;
                t.SobraValor += p.SobraValor;
                t.FaltaCuadradaUnidades += p.FaltaCuadradaUnidades;
                t.FaltaCuadradaValor += p.FaltaCuadradaValor;
                t.SobraCuadradaUnidades += p.SobraCuadradaUnidades;
                t.SobraCuadradaValor += p.SobraCuadradaValor;
            }
            Completar(t);
            return t;
        }

        private static string VacioANull(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        private static decimal Redondear(decimal importe) => RoundingHelper.Round(importe, 2);
    }
}
