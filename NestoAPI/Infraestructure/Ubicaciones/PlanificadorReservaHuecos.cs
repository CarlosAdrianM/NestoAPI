using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Ubicaciones
{
    /// <summary>Una fila de Ubicaciones de la que se puede reservar: estado 0 (en su hueco) o 2 (pendiente de ubicar).</summary>
    public class FilaUbicacionLibre
    {
        public int NumeroOrden { get; set; }
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        public string Producto { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int Cantidad { get; set; }
        public int Estado { get; set; }
        public DateTime FechaCreacion { get; set; }

        public HuecoUbicacion Hueco => HuecoUbicacion.De(Pasillo, Fila, Columna);
    }

    /// <summary>Lo que se coge de una fila libre.</summary>
    public class TomaDeHueco
    {
        public FilaUbicacionLibre Libre { get; set; }
        public int Cantidad { get; set; }
        /// <summary>Se coge la fila entera: la propia fila pasa a reserva (si no, se resta y se inserta la reserva aparte).</summary>
        public bool Entera => Libre != null && Cantidad == Libre.Cantidad;
    }

    /// <summary>
    /// NestoAPI#594: de qué filas libres sale cada reserva de reposición. Puro: sin base de datos. Reproduce el reparto de
    /// prdUbicarReposicion (tipo 1), leído el 06/10/26:
    /// <list type="number">
    /// <item>Si alguna fila tiene cantidad suficiente, se coge de UNA: con FIFO (Almacenes.FIFO = 1) la de FechaCreación
    /// más antigua y menor NºOrden; con LIFO, la más reciente y mayor NºOrden.</item>
    /// <item>Si ninguna llega, se va cogiendo de varias en el mismo orden (FIFO: de la más antigua a la más nueva) hasta
    /// cubrir la cantidad o quedarse sin filas.</item>
    /// <item>Si no hay ninguna, nada: la línea se queda «sin hueco».</item>
    /// </list>
    /// Estado 0 y 2 cuentan igual (el procedimiento tampoco los distingue). Las filas con cantidad ≤ 0 no se consideran
    /// (el procedimiento las podría coger y dejar una reserva a 0).
    /// </summary>
    public static class PlanificadorReservaHuecos
    {
        public static List<TomaDeHueco> Repartir(IEnumerable<FilaUbicacionLibre> libres, int cantidad, bool fifo)
        {
            var tomas = new List<TomaDeHueco>();
            if (cantidad <= 0)
            {
                return tomas;
            }
            List<FilaUbicacionLibre> ordenadas = Ordenar((libres ?? Enumerable.Empty<FilaUbicacionLibre>())
                .Where(l => l != null && l.Cantidad > 0 && (l.Estado == EstadosUbicacion.LIBRE || l.Estado == EstadosUbicacion.PENDIENTE_DE_UBICAR)), fifo);

            FilaUbicacionLibre suficiente = ordenadas.FirstOrDefault(l => l.Cantidad >= cantidad);
            if (suficiente != null)
            {
                tomas.Add(new TomaDeHueco { Libre = suficiente, Cantidad = cantidad });
                return tomas;
            }

            int falta = cantidad;
            foreach (FilaUbicacionLibre libre in ordenadas)
            {
                if (falta <= 0)
                {
                    break;
                }
                int trozo = Math.Min(falta, libre.Cantidad);
                tomas.Add(new TomaDeHueco { Libre = libre, Cantidad = trozo });
                falta -= trozo;
            }
            return tomas;
        }

        /// <summary>Las filas libres tras coger <paramref name="tomas"/> (para la siguiente línea del mismo producto).</summary>
        public static List<FilaUbicacionLibre> Descontar(IEnumerable<FilaUbicacionLibre> libres, IEnumerable<TomaDeHueco> tomas)
        {
            Dictionary<int, int> cogido = (tomas ?? Enumerable.Empty<TomaDeHueco>())
                .GroupBy(t => t.Libre.NumeroOrden).ToDictionary(g => g.Key, g => g.Sum(t => t.Cantidad));
            var quedan = new List<FilaUbicacionLibre>();
            foreach (FilaUbicacionLibre libre in libres ?? Enumerable.Empty<FilaUbicacionLibre>())
            {
                int menos = cogido.TryGetValue(libre.NumeroOrden, out int c) ? c : 0;
                if (libre.Cantidad - menos > 0)
                {
                    quedan.Add(new FilaUbicacionLibre
                    {
                        NumeroOrden = libre.NumeroOrden, Empresa = libre.Empresa, Almacen = libre.Almacen, Producto = libre.Producto,
                        Pasillo = libre.Pasillo, Fila = libre.Fila, Columna = libre.Columna, Cantidad = libre.Cantidad - menos,
                        Estado = libre.Estado, FechaCreacion = libre.FechaCreacion
                    });
                }
            }
            return quedan;
        }

        private static List<FilaUbicacionLibre> Ordenar(IEnumerable<FilaUbicacionLibre> libres, bool fifo)
        {
            return fifo
                ? libres.OrderBy(l => l.FechaCreacion).ThenBy(l => l.NumeroOrden).ToList()
                : libres.OrderByDescending(l => l.FechaCreacion).ThenByDescending(l => l.NumeroOrden).ToList();
        }
    }
}
