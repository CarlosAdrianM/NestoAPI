using NestoAPI.Models.Proyectos;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Proyectos
{
    /// <summary>NestoAPI#901: lo imputado a un proyecto agrupado por cuenta y lado (para los totales de la lista sin leer cada imputación).</summary>
    public class ResumenImputacionesProyecto
    {
        public int ProyectoId { get; set; }
        public string Cuenta { get; set; }
        /// <summary>1 = apunte al Debe (Debe &gt; Haber), −1 = al Haber.</summary>
        public int Lado { get; set; }
        public decimal Importe { get; set; }
        public int Numero { get; set; }
    }

    /// <summary>NestoAPI#901: LA REGLA (pura) de la cuenta de explotación, la lista con totales y la comparativa.</summary>
    public static class CalculoCuentaExplotacionProyecto
    {
        public static ProyectoDTO Dto(FilaProyecto p) => p == null ? null : new ProyectoDTO
        {
            Id = p.Id,
            Empresa = p.Empresa?.Trim(),
            Codigo = p.Codigo?.Trim(),
            Nombre = p.Nombre?.Trim(),
            Tipo = p.Tipo?.Trim(),
            FechaDesde = p.FechaDesde,
            FechaHasta = p.FechaHasta,
            Estado = p.Estado?.Trim(),
            Producto = p.Producto?.Trim(),
            EventoId = p.EventoId,
            PresupuestoIngresos = p.PresupuestoIngresos,
            PresupuestoGastos = p.PresupuestoGastos,
            Comentarios = p.Comentarios,
            Usuario = p.Usuario?.Trim(),
            FechaAlta = p.FechaAlta == default ? (System.DateTime?)null : p.FechaAlta,
            UsuarioModificacion = p.UsuarioModificacion?.Trim(),
            FechaModificacion = p.FechaModificacion
        };

        /// <summary>Totales de cada proyecto a partir del resumen por cuenta y lado (los abonos restan).</summary>
        public static void PonerTotales(ProyectoDTO dto, IEnumerable<ResumenImputacionesProyecto> resumen)
        {
            decimal ingresos = 0, gastos = 0;
            int numero = 0;
            foreach (ResumenImputacionesProyecto r in resumen ?? Enumerable.Empty<ResumenImputacionesProyecto>())
            {
                numero += r.Numero;
                decimal efecto = r.Importe * ReglasProyectos.Signo(r.Cuenta, r.Lado > 0 ? 1 : 0, r.Lado > 0 ? 0 : 1);
                switch (ReglasProyectos.Naturaleza(r.Cuenta))
                {
                    case NaturalezaApunte.Ingreso: ingresos += efecto; break;
                    case NaturalezaApunte.Gasto: gastos += efecto; break;
                }
            }
            dto.TotalIngresos = ingresos;
            dto.TotalGastos = gastos;
            dto.Resultado = ReglasProyectos.Resultado(ingresos, gastos);
            dto.NumeroImputaciones = numero;
        }

        public static CuentaExplotacionProyectoDTO Calcular(FilaProyecto proyecto, IEnumerable<FilaImputacion> imputaciones)
        {
            List<FilaImputacion> lista = (imputaciones ?? Enumerable.Empty<FilaImputacion>()).ToList();
            ProyectoDTO dto = Dto(proyecto);
            var cuenta = new CuentaExplotacionProyectoDTO { Proyecto = dto };
            cuenta.Ingresos = Lineas(lista, NaturalezaApunte.Ingreso);
            cuenta.Gastos = Lineas(lista, NaturalezaApunte.Gasto);
            cuenta.TotalIngresos = cuenta.Ingresos.Sum(l => l.Importe);
            cuenta.TotalGastos = cuenta.Gastos.Sum(l => l.Importe);
            cuenta.Resultado = ReglasProyectos.Resultado(cuenta.TotalIngresos, cuenta.TotalGastos);
            cuenta.Margen = ReglasProyectos.Margen(cuenta.TotalIngresos, cuenta.TotalGastos);
            dto.TotalIngresos = cuenta.TotalIngresos;
            dto.TotalGastos = cuenta.TotalGastos;
            dto.Resultado = cuenta.Resultado;
            dto.NumeroImputaciones = lista.Count;

            if (proyecto.PresupuestoIngresos.HasValue || proyecto.PresupuestoGastos.HasValue)
            {
                decimal presupuestoResultado = (proyecto.PresupuestoIngresos ?? 0) - (proyecto.PresupuestoGastos ?? 0);
                cuenta.Presupuesto = new PresupuestoFrenteARealDTO
                {
                    PresupuestoIngresos = proyecto.PresupuestoIngresos,
                    PresupuestoGastos = proyecto.PresupuestoGastos,
                    PresupuestoResultado = presupuestoResultado,
                    DesviacionIngresos = proyecto.PresupuestoIngresos.HasValue ? cuenta.TotalIngresos - proyecto.PresupuestoIngresos.Value : (decimal?)null,
                    DesviacionGastos = proyecto.PresupuestoGastos.HasValue ? cuenta.TotalGastos - proyecto.PresupuestoGastos.Value : (decimal?)null,
                    DesviacionResultado = cuenta.Resultado - presupuestoResultado
                };
            }

            cuenta.Detalle = lista
                .Where(i => ReglasProyectos.Naturaleza(i.Cuenta) != NaturalezaApunte.NoImputable)
                .OrderBy(i => i.FechaApunte).ThenBy(i => i.NumeroOrdenApunte)
                .Select(i => new DetalleCuentaExplotacionDTO
                {
                    IdImputacion = i.Id,
                    NumeroOrdenApunte = i.NumeroOrdenApunte,
                    Fecha = i.FechaApunte,
                    Cuenta = i.Cuenta?.Trim(),
                    NombreCuenta = i.NombreCuenta?.Trim(),
                    Naturaleza = ReglasProyectos.Naturaleza(i.Cuenta).ToString(),
                    Documento = i.Documento?.Trim(),
                    Concepto = i.Concepto?.Trim(),
                    Asiento = i.Asiento,
                    Tercero = i.Tercero?.Trim(),
                    TipoTercero = i.TipoTercero,
                    NombreTercero = i.NombreTercero?.Trim(),
                    Debe = i.Debe,
                    Haber = i.Haber,
                    ImporteApunte = ReglasProyectos.ImporteApunte(i.Debe, i.Haber),
                    Importe = i.Importe,
                    Efecto = ReglasProyectos.Efecto(i),
                    Porcentaje = ReglasProyectos.Porcentaje(i.Importe, ReglasProyectos.ImporteApunte(i.Debe, i.Haber)),
                    Comentario = i.Comentario,
                    Usuario = i.Usuario?.Trim(),
                    FechaImputacion = i.Fecha
                }).ToList();
            return cuenta;
        }

        private static List<LineaCuentaExplotacionDTO> Lineas(List<FilaImputacion> lista, NaturalezaApunte naturaleza) => lista
            .Where(i => ReglasProyectos.Naturaleza(i.Cuenta) == naturaleza)
            .GroupBy(i => i.Cuenta?.Trim())
            .Select(g => new LineaCuentaExplotacionDTO
            {
                Cuenta = g.Key,
                NombreCuenta = g.Select(i => i.NombreCuenta?.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n)),
                Importe = g.Sum(ReglasProyectos.Efecto),
                NumeroApuntes = g.Select(i => i.NumeroOrdenApunte).Distinct().Count()
            })
            .OrderBy(l => l.Cuenta)
            .ToList();

        /// <summary>Las cuentas de explotación lado a lado (una columna por proyecto, en el orden recibido).</summary>
        public static ComparativaProyectosDTO Comparar(IList<FilaProyecto> proyectos, IEnumerable<FilaImputacion> imputaciones)
        {
            List<FilaImputacion> todas = (imputaciones ?? Enumerable.Empty<FilaImputacion>()).ToList();
            var comparativa = new ComparativaProyectosDTO();
            var cuentas = new List<CuentaExplotacionProyectoDTO>();
            foreach (FilaProyecto p in proyectos)
            {
                CuentaExplotacionProyectoDTO c = Calcular(p, todas.Where(i => i.ProyectoId == p.Id));
                cuentas.Add(c);
                comparativa.Proyectos.Add(c.Proyecto);
                comparativa.TotalesIngresos.Add(c.TotalIngresos);
                comparativa.TotalesGastos.Add(c.TotalGastos);
                comparativa.Resultados.Add(c.Resultado);
                comparativa.Margenes.Add(c.Margen);
            }
            comparativa.Ingresos = Filas(cuentas, c => c.Ingresos);
            comparativa.Gastos = Filas(cuentas, c => c.Gastos);
            return comparativa;
        }

        private static List<FilaComparativaProyectosDTO> Filas(List<CuentaExplotacionProyectoDTO> cuentas,
            System.Func<CuentaExplotacionProyectoDTO, List<LineaCuentaExplotacionDTO>> lineas)
        {
            var nombres = new Dictionary<string, string>();
            foreach (LineaCuentaExplotacionDTO l in cuentas.SelectMany(lineas))
            {
                if (!nombres.ContainsKey(l.Cuenta) || string.IsNullOrEmpty(nombres[l.Cuenta]))
                {
                    nombres[l.Cuenta] = l.NombreCuenta;
                }
            }
            return nombres.Keys.OrderBy(k => k).Select(k => new FilaComparativaProyectosDTO
            {
                Cuenta = k,
                NombreCuenta = nombres[k],
                Importes = cuentas.Select(c => lineas(c).Where(l => l.Cuenta == k).Sum(l => l.Importe)).ToList()
            }).ToList();
        }
    }
}
