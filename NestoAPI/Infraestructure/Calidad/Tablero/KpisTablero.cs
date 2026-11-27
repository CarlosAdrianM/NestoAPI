using NestoAPI.Infraestructure.Calidad.PedidoPerfecto;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Calidad.Tablero
{
    // Los primeros KPI del tablero (Carlos 27/11/26: «los que más me interesan inicialmente son los que afectan a ventas o a pérdida
    // desconocida. Si algo hace "desaparecer" un producto, quiero medirlo»). Cada uno reutiliza el cálculo que ya existía.

    internal static class TextosKpi
    {
        internal static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        internal static string Euros(decimal valor) => valor.ToString("#,##0.00", Es) + " €";

        internal static decimal Redondear(decimal valor) => RoundingHelper.Round(valor, 2);
    }

    /// <summary>
    /// Pérdida desconocida de la semana por almacén (NestoAPI#893): LA MISMA consulta (<see cref="GestorPerdidaDesconocida.SQL_PERDIDA_DESCONOCIDA"/>) y
    /// LA MISMA regla (<see cref="GestorPerdidaDesconocida.Construir"/>, con su cuadre entre almacenes) que el informe, con la semana como periodo
    /// único. Valor = la pérdida de verdad (lo que falta y no cuadra con lo que sobra en otro almacén) a coste.
    /// </summary>
    public class KpiPerdidaDesconocida : IKpiTablero
    {
        public const string CLAVE = "PerdidaDesconocida";

        public string Clave => CLAVE;
        public string Nombre => "Pérdida desconocida";
        public string Descripcion => "Lo que ha desaparecido esta semana en su almacén sin explicación (inventarios y «No está» o diferencias de reposición), " +
            "a precio de coste, después de cuadrar lo que falta en un almacén con lo que sobra en otro del mismo producto.";
        public IReadOnlyList<TipoDuenoKpi> TiposDueno { get; } = new[] { TipoDuenoKpi.Almacen };
        public UnidadKpi Unidad => UnidadKpi.Euros;
        public SentidoKpi Sentido => SentidoKpi.MenosEsMejor;
        public int DiasMaduracion => 0;
        public string PorQueMadura => null;
        public bool TieneFuente => true;

        public List<ValorKpi> Calcular(PeriodoKpi periodo, IFuentesKpi fuentes)
        {
            PerdidaDesconocidaDTO informe = InformeDeLaSemana(fuentes.MovimientosStockSinExplicar(periodo.Lunes, periodo.Hasta), periodo);
            return GestorPerdidaDesconocida.ALMACENES.Select(a =>
            {
                PeriodoPerdidaDesconocidaDTO p = informe.Almacenes.FirstOrDefault(x => x.Almacen == a)?.Acumulado ?? new PeriodoPerdidaDesconocidaDTO();
                return new ValorKpi
                {
                    TipoDueno = TipoDuenoKpi.Almacen,
                    Dueno = a,
                    Valor = p.PerdidaRealValor,
                    Numerador = p.PerdidaRealUnidades,
                    Detalle = SemanaKpi.Recortar($"Faltan {p.FaltaUnidades} ud. ({TextosKpi.Euros(p.FaltaValor)}); {p.FaltaCuadradaUnidades} ud. cuadran con lo que sobra en " +
                        $"otro almacén. Sobra sin cuadrar: {p.SobraRealUnidades} ud. ({TextosKpi.Euros(p.SobraRealValor)}).")
                };
            }).ToList();
        }

        /// <summary>El informe de #893 con la semana como único periodo (todas las filas al «mes 1», para que no la parta un cambio de mes o de año).</summary>
        internal static PerdidaDesconocidaDTO InformeDeLaSemana(IEnumerable<FilaPerdidaDesconocidaDTO> filas, PeriodoKpi periodo)
        {
            List<FilaPerdidaDesconocidaDTO> unPeriodo = (filas ?? Enumerable.Empty<FilaPerdidaDesconocidaDTO>()).Where(f => f != null)
                .Select(f => new FilaPerdidaDesconocidaDTO
                {
                    Almacen = f.Almacen,
                    Mes = 1,
                    Producto = f.Producto,
                    Diario = f.Diario,
                    Altas = f.Altas,
                    Bajas = f.Bajas,
                    Nombre = f.Nombre,
                    Familia = f.Familia,
                    PrecioMedio = f.PrecioMedio
                }).ToList();
            return GestorPerdidaDesconocida.Construir(unPeriodo, new DateTime(periodo.Lunes.Year, 1, 1));
        }
    }

    /// <summary>
    /// Diferencias de inventario de la semana por almacén: lo que descuadró al contar (apuntes «_RegInvent» de la consulta de #893, que es la
    /// fecha del inventario contabilizado), sobras y faltas sin netear entre sí: Valor = Σ (altas + bajas) × coste; Numerador = productos descuadrados.
    /// </summary>
    public class KpiDiferenciasInventario : IKpiTablero
    {
        public const string CLAVE = "DiferenciasInventario";

        public string Clave => CLAVE;
        public string Nombre => "Diferencias de inventario";
        public string Descripcion => "Lo que no cuadró al contar en los inventarios contabilizados esta semana en su almacén, a precio de coste " +
            "(lo que faltaba más lo que sobraba), y en cuántos productos.";
        public IReadOnlyList<TipoDuenoKpi> TiposDueno { get; } = new[] { TipoDuenoKpi.Almacen };
        public UnidadKpi Unidad => UnidadKpi.Euros;
        public SentidoKpi Sentido => SentidoKpi.MenosEsMejor;
        public int DiasMaduracion => 0;
        public string PorQueMadura => null;
        public bool TieneFuente => true;

        public List<ValorKpi> Calcular(PeriodoKpi periodo, IFuentesKpi fuentes)
        {
            List<FilaPerdidaDesconocidaDTO> inventario = (fuentes.MovimientosStockSinExplicar(periodo.Lunes, periodo.Hasta) ?? new List<FilaPerdidaDesconocidaDTO>())
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Producto)
                    && string.Equals(f.Diario?.Trim(), GestorPerdidaDesconocida.DIARIO_INVENTARIO, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return GestorPerdidaDesconocida.ALMACENES.Select(a =>
            {
                var productos = inventario.Where(f => string.Equals(f.Almacen?.Trim(), a, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(f => f.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => new
                    {
                        Altas = g.Sum(f => Math.Max(0, f.Altas)),
                        Bajas = g.Sum(f => Math.Max(0, f.Bajas)),
                        Coste = g.Select(f => f.PrecioMedio ?? 0m).FirstOrDefault(c => c != 0m)
                    })
                    .Where(p => p.Altas + p.Bajas > 0)
                    .ToList();
                decimal faltaValor = TextosKpi.Redondear(productos.Sum(p => p.Bajas * p.Coste));
                decimal sobraValor = TextosKpi.Redondear(productos.Sum(p => p.Altas * p.Coste));
                return new ValorKpi
                {
                    TipoDueno = TipoDuenoKpi.Almacen,
                    Dueno = a,
                    Valor = faltaValor + sobraValor,
                    Numerador = productos.Count,
                    Detalle = SemanaKpi.Recortar($"{productos.Count} productos descuadrados: faltaban {productos.Sum(p => p.Bajas)} ud. ({TextosKpi.Euros(faltaValor)}) " +
                        $"y sobraban {productos.Sum(p => p.Altas)} ud. ({TextosKpi.Euros(sobraValor)}).")
                };
            }).ToList();
        }
    }

    /// <summary>Unidades de una diferencia de reposición imputadas a un almacén.</summary>
    public class ImputacionDiferenciaKpi
    {
        public string Almacen { get; set; }
        public int Unidades { get; set; }
        public decimal Valor { get; set; }
        public bool EsFalta { get; set; }
    }

    public class ResultadoQuienPierdeReposicion
    {
        public List<ImputacionDiferenciaKpi> Imputadas { get; set; } = new List<ImputacionDiferenciaKpi>();
        /// <summary>Unidades de diferencias aún pendientes (o sin a quién imputarlas): no cuentan para nadie todavía.</summary>
        public int UnidadesSinImputar { get; set; }
        public int DiferenciasSinImputar { get; set; }
    }

    /// <summary>
    /// NestoAPI#906 aplicado al tablero (puro): a quién se le imputa cada unidad de una diferencia de reposición. Resoluciones de siempre
    /// (Confirmada, NoEstaba → origen; ErrorRecepcion, Sobraba → destino), con la MISMA regla que el indicador de errores
    /// (<see cref="ReglaIndicadorErrores.ErroresDe"/>); «Desacuerdo», lo que decidió <see cref="ReglaImputacionDesacuerdo"/> unidad a unidad
    /// (DiferenciasReposicionImputaciones.AlmacenImputado: quien se queda la baja). Pendientes: sin imputar.
    /// </summary>
    public static class ReglaQuienPierdeReposicion
    {
        public static ResultadoQuienPierdeReposicion Imputar(IEnumerable<DiferenciaReposicionKpi> diferencias, IEnumerable<ImputacionReposicionKpi> imputaciones)
        {
            var resultado = new ResultadoQuienPierdeReposicion();
            List<ImputacionReposicionKpi> desacuerdos = (imputaciones ?? Enumerable.Empty<ImputacionReposicionKpi>()).Where(i => i != null).ToList();
            foreach (DiferenciaReposicionKpi d in (diferencias ?? Enumerable.Empty<DiferenciaReposicionKpi>()).Where(x => x != null && x.Diferencia != 0))
            {
                int unidades = Math.Abs(d.Diferencia);
                bool falta = d.Diferencia < 0;
                string resolucion = d.Resolucion?.Trim();
                if (string.Equals(resolucion, ResolucionesDiferenciaReposicion.DESACUERDO, StringComparison.OrdinalIgnoreCase))
                {
                    int imputadas = 0;
                    foreach (ImputacionReposicionKpi i in desacuerdos.Where(x => x.DiferenciaId == d.Id && !string.IsNullOrWhiteSpace(x.Almacen) && x.Unidades > 0))
                    {
                        int u = Math.Min(i.Unidades, unidades - imputadas);
                        if (u <= 0)
                        {
                            break;
                        }
                        Anadir(resultado, i.Almacen, u, d.PrecioMedio, falta);
                        imputadas += u;
                    }
                    if (imputadas < unidades)
                    {
                        resultado.UnidadesSinImputar += unidades - imputadas;
                        resultado.DiferenciasSinImputar++;
                    }
                    continue;
                }
                List<string> culpables = string.IsNullOrEmpty(resolucion) ? new List<string>()
                    : ReglaIndicadorErrores.ErroresDe(new DiferenciaParaIndicador { Id = d.Id, Origen = d.Origen, Destino = d.Destino, Diferencia = d.Diferencia, Resolucion = resolucion });
                if (culpables.Count == 1)
                {
                    Anadir(resultado, culpables[0], unidades, d.PrecioMedio, falta);
                }
                else
                {
                    resultado.UnidadesSinImputar += unidades;
                    resultado.DiferenciasSinImputar++;
                }
            }
            return resultado;
        }

        private static void Anadir(ResultadoQuienPierdeReposicion r, string almacen, int unidades, decimal coste, bool falta) =>
            r.Imputadas.Add(new ImputacionDiferenciaKpi
            {
                Almacen = almacen.Trim().ToUpperInvariant(),
                Unidades = unidades,
                Valor = TextosKpi.Redondear(unidades * coste),
                EsFalta = falta
            });
    }

    /// <summary>
    /// Diferencias de reposición de la semana (recibidas esa semana) por almacén o tienda, imputadas a QUIEN PIERDE con la regla de #906
    /// (<see cref="ReglaQuienPierdeReposicion"/>). Valor = unidades imputadas a coste; Numerador = unidades.
    /// </summary>
    public class KpiDiferenciasReposicion : IKpiTablero
    {
        public const string CLAVE = "DiferenciasReposicion";
        public const int DIAS_MADURACION = 7;

        public string Clave => CLAVE;
        public string Nombre => "Diferencias de reposición";
        public string Descripcion => "Lo que no llegó o llegó de más en las reposiciones recibidas esta semana y se le imputa a su almacén " +
            "(por cómo se resolvió la diferencia o, si hubo desacuerdo, por la regla de quién pierde), a precio de coste.";
        public IReadOnlyList<TipoDuenoKpi> TiposDueno { get; } = new[] { TipoDuenoKpi.Almacen };
        public UnidadKpi Unidad => UnidadKpi.Euros;
        public SentidoKpi Sentido => SentidoKpi.MenosEsMejor;
        public int DiasMaduracion => DIAS_MADURACION;
        public string PorQueMadura => "Hay que esperar a que se resuelvan las diferencias (comprobación en el origen y, si hay discusión, su plazo de 48 horas).";
        public bool TieneFuente => true;

        public List<ValorKpi> Calcular(PeriodoKpi periodo, IFuentesKpi fuentes)
        {
            LecturaDiferenciasReposicionKpi lectura = fuentes.DiferenciasReposicion(periodo.Lunes, periodo.Hasta);
            if (lectura == null || !lectura.HayDiferencias)
            {
                return new List<ValorKpi>();
            }
            ResultadoQuienPierdeReposicion r = ReglaQuienPierdeReposicion.Imputar(lectura.Diferencias, lectura.Imputaciones);
            IEnumerable<string> almacenes = GestorPerdidaDesconocida.ALMACENES
                .Concat(r.Imputadas.Select(i => i.Almacen))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            string sinImputar = r.UnidadesSinImputar == 0 ? string.Empty
                : $" Quedan {r.UnidadesSinImputar} ud. de {r.DiferenciasSinImputar} diferencias sin resolver, que aún no cuentan para nadie.";
            return almacenes.Select(a =>
            {
                List<ImputacionDiferenciaKpi> suyas = r.Imputadas.Where(i => string.Equals(i.Almacen, a, StringComparison.OrdinalIgnoreCase)).ToList();
                int faltas = suyas.Where(i => i.EsFalta).Sum(i => i.Unidades);
                int sobras = suyas.Where(i => !i.EsFalta).Sum(i => i.Unidades);
                return new ValorKpi
                {
                    TipoDueno = TipoDuenoKpi.Almacen,
                    Dueno = a.ToUpperInvariant(),
                    Valor = suyas.Sum(i => i.Valor),
                    Numerador = faltas + sobras,
                    Detalle = SemanaKpi.Recortar($"Se le imputan {faltas} ud. que no llegaron y {sobras} ud. que llegaron de más.{sinImputar}")
                };
            }).ToList();
        }
    }

    /// <summary>
    /// Entregas incompletas: de los pedidos SERVIDOS esa semana (primer albarán en la semana; pedidos del universo del pedido perfecto metidos
    /// hasta <see cref="DIAS_ATRAS_PEDIDO"/> días antes del lunes), los que tienen nota de entrega de lo pendiente (#542, motivo EntregaIncompleta)
    /// o entrega incompleta de nuestro reparto (#880, motivo EntregaIncompletaDelReparto). Por empresa y por almacén de salida (el de sus líneas).
    /// </summary>
    public class KpiEntregasIncompletas : IKpiTablero
    {
        public const string CLAVE = "EntregasIncompletas";
        public const int DIAS_MADURACION = 3;
        /// <summary>Se buscan los pedidos servidos esa semana entre los metidos hasta 4 semanas antes (lo más viejo se escapa: se dice).</summary>
        public const int DIAS_ATRAS_PEDIDO = 28;

        public string Clave => CLAVE;
        public string Nombre => "Entregas incompletas";
        public string Descripcion => "De los pedidos servidos esta semana, cuántos no se entregaron enteros: se hizo el albarán con algo pendiente " +
            "de entregar (nota de entrega aparte) o nuestro reparto llegó con bultos de menos.";
        public IReadOnlyList<TipoDuenoKpi> TiposDueno { get; } = new[] { TipoDuenoKpi.Empresa, TipoDuenoKpi.Almacen };
        public UnidadKpi Unidad => UnidadKpi.Porcentaje;
        public SentidoKpi Sentido => SentidoKpi.MenosEsMejor;
        public int DiasMaduracion => DIAS_MADURACION;
        public string PorQueMadura => "Lo servido el viernes se entrega el lunes o el martes: hay que esperar a que el reparto diga si faltó algún bulto.";
        public bool TieneFuente => true;

        public List<ValorKpi> Calcular(PeriodoKpi periodo, IFuentesKpi fuentes)
        {
            List<HechosPedidoPerfecto> servidos = (fuentes.PedidosPerfecto(periodo.Lunes.AddDays(-DIAS_ATRAS_PEDIDO), periodo.Hasta)
                    ?? new List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>>())
                .Select(p => p.Key)
                .Where(h => h != null && h.PrimerAlbaran.HasValue && h.PrimerAlbaran.Value.Date >= periodo.Lunes && h.PrimerAlbaran.Value.Date < periodo.Hasta)
                .ToList();
            var valores = new List<ValorKpi> { Valor(TipoDuenoKpi.Empresa, Constantes.Empresas.EMPRESA_POR_DEFECTO, servidos) };
            valores.AddRange(servidos
                .GroupBy(h => string.IsNullOrWhiteSpace(h.Almacen) ? Constantes.Almacenes.ALGETE : h.Almacen.Trim().ToUpperInvariant())
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => Valor(TipoDuenoKpi.Almacen, g.Key, g.ToList())));
            return valores;
        }

        internal static bool EsIncompleta(HechosPedidoPerfecto h) => h.TieneNotaEntregaPendiente || h.EntregaIncompleta;

        private static ValorKpi Valor(TipoDuenoKpi tipo, string dueno, List<HechosPedidoPerfecto> servidos)
        {
            int conNota = servidos.Count(h => h.TieneNotaEntregaPendiente);
            int reparto = servidos.Count(h => h.EntregaIncompleta);
            int incompletos = servidos.Count(EsIncompleta);
            return new ValorKpi
            {
                TipoDueno = tipo,
                Dueno = dueno,
                Valor = SemanaKpi.Porcentaje(incompletos, servidos.Count),
                Numerador = incompletos,
                Denominador = servidos.Count,
                Detalle = $"{incompletos} de {servidos.Count} pedidos servidos: {conNota} con nota de entrega de lo pendiente y {reparto} con bultos de menos en nuestro reparto."
            };
        }
    }

    /// <summary>
    /// Pedido perfecto por vendedor (y de la empresa): porcentaje de los pedidos METIDOS esa semana (universo y regla de
    /// <see cref="ReglaPedidoPerfecto"/>) que son perfectos sin contar los motivos informativos (no se cobró al contado, tiene copia: no son
    /// culpa de nadie). Madura <see cref="ReglaPedidoPerfecto.DIAS_PLAZO_SIN_SERVIR"/> días, el plazo más largo de la regla («sin servir»).
    /// </summary>
    public class KpiPedidoPerfecto : IKpiTablero
    {
        public const string CLAVE = "PedidoPerfecto";

        public string Clave => CLAVE;
        public string Nombre => "Pedido perfecto";
        public string Descripcion => "De los pedidos metidos esta semana, cuántos fueron perfectos: había stock de todo, salió al día siguiente " +
            "de una vez, se entregó entero y no hubo ningún problema después (rectificativas, impagados, incidencias de la agencia…).";
        public IReadOnlyList<TipoDuenoKpi> TiposDueno { get; } = new[] { TipoDuenoKpi.Vendedor, TipoDuenoKpi.Empresa };
        public UnidadKpi Unidad => UnidadKpi.Porcentaje;
        public SentidoKpi Sentido => SentidoKpi.MasEsMejor;
        public int DiasMaduracion => ReglaPedidoPerfecto.DIAS_PLAZO_SIN_SERVIR;
        public string PorQueMadura => $"Un pedido no se puede dar por perfecto hasta que pasa el plazo más largo de la regla: {ReglaPedidoPerfecto.DIAS_PLAZO_SIN_SERVIR} días " +
            "para saber si se quedó sin servir.";
        public bool TieneFuente => true;

        public List<ValorKpi> Calcular(PeriodoKpi periodo, IFuentesKpi fuentes)
        {
            List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>> pedidos = (fuentes.PedidosPerfecto(periodo.Lunes, periodo.Hasta)
                    ?? new List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>>())
                .Where(p => p.Key != null && p.Value != null)
                .ToList();
            var valores = new List<ValorKpi> { Valor(TipoDuenoKpi.Empresa, Constantes.Empresas.EMPRESA_POR_DEFECTO, pedidos) };
            valores.AddRange(pedidos
                .Where(p => !string.IsNullOrWhiteSpace(p.Key.Vendedor))
                .GroupBy(p => p.Key.Vendedor.Trim().ToUpperInvariant())
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => Valor(TipoDuenoKpi.Vendedor, g.Key, g.ToList())));
            return valores;
        }

        /// <summary>El motivo que es error y más pedidos tiene (en empate, el de número menor). Null si no hay ninguno.</summary>
        internal static KeyValuePair<MotivoPedidoPerfecto, int>? MotivoQueMasResta(IEnumerable<EvaluacionPedidoPerfecto> evaluaciones) =>
            evaluaciones.SelectMany(e => e.Motivos.Distinct())
                .Where(InfoMotivosPedidoPerfecto.EsError)
                .GroupBy(m => m)
                .Select(g => new KeyValuePair<MotivoPedidoPerfecto, int>(g.Key, g.Count()))
                .OrderByDescending(k => k.Value).ThenBy(k => (int)k.Key)
                .Cast<KeyValuePair<MotivoPedidoPerfecto, int>?>()
                .FirstOrDefault();

        private static ValorKpi Valor(TipoDuenoKpi tipo, string dueno, List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>> pedidos)
        {
            int perfectos = pedidos.Count(p => p.Value.PerfectoSinContarInformativos);
            KeyValuePair<MotivoPedidoPerfecto, int>? peor = MotivoQueMasResta(pedidos.Select(p => p.Value));
            string detalle = peor.HasValue
                ? $"Lo que más resta: {peor.Value.Key} ({peor.Value.Value} de {pedidos.Count} pedidos). {InfoMotivosPedidoPerfecto.Texto(peor.Value.Key)}"
                : $"{perfectos} de {pedidos.Count} pedidos perfectos.";
            return new ValorKpi
            {
                TipoDueno = tipo,
                Dueno = dueno,
                Valor = SemanaKpi.Porcentaje(perfectos, pedidos.Count),
                Numerador = perfectos,
                Denominador = pedidos.Count,
                Detalle = SemanaKpi.Recortar(detalle)
            };
        }
    }

    /// <summary>
    /// Reenviado sin coste: SIN FUENTE todavía. Se está programando en otra rama el indicador <c>ErrorDeEnvioReclamado</c> del corte de almacén
    /// de #914; hasta que llegue, el tablero lo lista como «todavía no disponible» y el job no lo calcula.
    /// </summary>
    public class KpiReenviadoSinCoste : IKpiTablero
    {
        public const string CLAVE = "ReenviadoSinCoste";

        public string Clave => CLAVE;
        public string Nombre => "Reenviado sin coste";
        public string Descripcion => "Lo que hemos tenido que volver a enviar sin cobrarlo porque el cliente reclamó un error nuestro al preparar.";
        public IReadOnlyList<TipoDuenoKpi> TiposDueno { get; } = new[] { TipoDuenoKpi.Preparador };
        public UnidadKpi Unidad => UnidadKpi.Euros;
        public SentidoKpi Sentido => SentidoKpi.MenosEsMejor;
        /// <summary>Depende de que el cliente reclame: provisional (lo decidirá la rama del indicador).</summary>
        public int DiasMaduracion => 14;
        public string PorQueMadura => "Depende de que el cliente reclame: hay que esperar a que le llegue el pedido y lo revise.";
        public bool TieneFuente => false;

        public List<ValorKpi> Calcular(PeriodoKpi periodo, IFuentesKpi fuentes)
        {
            // TODO NestoAPI#914 (corte de almacén): enchufar aquí el lector del indicador «ErrorDeEnvioReclamado» cuando su rama llegue a master
            // (un método nuevo en IFuentesKpi que devuelva, por preparador de Ariadna y semana, el coste de lo reenviado sin cobrar). Entonces:
            // TieneFuente = true, DiasMaduracion = el plazo de reclamación que fije ese indicador y, si hace falta, TipoDueno Almacen además.
            return new List<ValorKpi>();
        }
    }
}
