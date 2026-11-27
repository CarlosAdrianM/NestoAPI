using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad.PedidoPerfecto;
using NestoAPI.Infraestructure.Calidad.Tablero;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>Fuentes simuladas para los KPI: lo que devolverían las consultas.</summary>
    internal class FuentesKpiFalsas : IFuentesKpi
    {
        public List<FilaPerdidaDesconocidaDTO> Stock { get; set; } = new List<FilaPerdidaDesconocidaDTO>();
        public LecturaDiferenciasReposicionKpi Reposicion { get; set; } = new LecturaDiferenciasReposicionKpi { HayDiferencias = true };
        public List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>> Pedidos { get; set; } =
            new List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>>();
        public List<string> Llamadas { get; } = new List<string>();

        public List<FilaPerdidaDesconocidaDTO> MovimientosStockSinExplicar(DateTime desde, DateTime hasta)
        {
            Llamadas.Add($"stock {desde:dd/MM}-{hasta:dd/MM}");
            return Stock;
        }

        public LecturaDiferenciasReposicionKpi DiferenciasReposicion(DateTime desde, DateTime hasta)
        {
            Llamadas.Add($"repo {desde:dd/MM}-{hasta:dd/MM}");
            return Reposicion;
        }

        public List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>> PedidosPerfecto(DateTime desde, DateTime hasta)
        {
            Llamadas.Add($"pedidos {desde:dd/MM}-{hasta:dd/MM}");
            return Pedidos.Where(p => p.Key.Fecha >= desde && p.Key.Fecha < hasta).ToList();
        }
    }

    /// <summary>NestoAPI#914, tablero: cada KPI con sus fuentes simuladas y LA REGLA de quién pierde en las reposiciones.</summary>
    [TestClass]
    public class KpisTableroTests
    {
        private static readonly DateTime LUNES = new DateTime(2026, 11, 16);
        private static readonly PeriodoKpi SEMANA = new PeriodoKpi(LUNES);

        private static FilaPerdidaDesconocidaDTO Fila(string almacen, string producto, string diario, int altas, int bajas, decimal coste, int mes = 11) =>
            new FilaPerdidaDesconocidaDTO { Almacen = almacen, Mes = mes, Producto = producto, Diario = diario, Altas = altas, Bajas = bajas, PrecioMedio = coste };

        private static KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto> Pedido(int numero, DateTime fecha, string vendedor,
            params MotivoPedidoPerfecto[] motivos) =>
            new KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>(
                new HechosPedidoPerfecto { Pedido = numero, Fecha = fecha, Vendedor = vendedor },
                new EvaluacionPedidoPerfecto { Motivos = motivos.ToList() });

        [TestMethod]
        public void Catalogo_ClavesUnicasYCadaKpiConSuTipo()
        {
            List<string> claves = CatalogoKpiTablero.Todos.Select(k => k.Clave).ToList();

            Assert.AreEqual(claves.Count, claves.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            CollectionAssert.IsSubsetOf(new[] { "PerdidaDesconocida", "DiferenciasReposicion", "DiferenciasInventario", "EntregasIncompletas", "PedidoPerfecto", "ReenviadoSinCoste" }, claves);
            Assert.IsTrue(CatalogoKpiTablero.Todos.All(k => k.TiposDueno.Count > 0 && k.Clave.Length <= 40));
            Assert.IsTrue(CatalogoKpiTablero.Todos.Where(k => k.DiasMaduracion > 0).All(k => !string.IsNullOrWhiteSpace(k.PorQueMadura)));
            Assert.AreSame(CatalogoKpiTablero.Todos[0], CatalogoKpiTablero.Buscar("perdidadesconocida"));
        }

        [TestMethod]
        public void PerdidaDesconocida_LaFaltaQueSobraEnOtroAlmacenCuadra_ySoloCuentaLaDeVerdad()
        {
            var fuentes = new FuentesKpiFalsas
            {
                Stock =
                {
                    Fila("ALG", "17404", "_RegInvent", 0, 3, 10m),
                    Fila("REI", "17404", "_ErrRepo", 1, 0, 10m),   // 1 de las 3 aparece en Reina: cuadra
                    Fila("ALC", "40372", "_RegInvent", 0, 2, 5.5m),
                    Fila("ALG", "99999", "_Santi", 0, 50, 1m)        // diario a mano sin clasificar: no cuenta
                }
            };

            List<ValorKpi> valores = new KpiPerdidaDesconocida().Calcular(SEMANA, fuentes);

            Assert.AreEqual(3, valores.Count);
            ValorKpi alg = valores.Single(v => v.Dueno == "ALG");
            Assert.AreEqual(TipoDuenoKpi.Almacen, alg.TipoDueno);
            Assert.AreEqual(20m, alg.Valor);      // 2 ud. × 10 €
            Assert.AreEqual(2m, alg.Numerador);
            StringAssert.Contains(alg.Detalle, "1 ud. cuadran");
            Assert.AreEqual(11m, valores.Single(v => v.Dueno == "ALC").Valor);
            Assert.AreEqual(0m, valores.Single(v => v.Dueno == "REI").Valor);
            CollectionAssert.AreEqual(new[] { "stock 16/11-23/11" }, fuentes.Llamadas);
        }

        [TestMethod]
        public void PerdidaDesconocida_SemanaQueCruzaElAnio_NoPierdeLasFilasDeDiciembre()
        {
            var periodo = new PeriodoKpi(new DateTime(2026, 12, 28));
            var fuentes = new FuentesKpiFalsas
            {
                Stock = { Fila("ALG", "1", "_RegInvent", 0, 1, 10m, mes: 12), Fila("ALG", "2", "_RegInvent", 0, 1, 10m, mes: 1) }
            };

            ValorKpi alg = new KpiPerdidaDesconocida().Calcular(periodo, fuentes).Single(v => v.Dueno == "ALG");

            Assert.AreEqual(20m, alg.Valor);
        }

        [TestMethod]
        public void DiferenciasInventario_SoloElInventario_SobrasYFaltasSinNetear()
        {
            var fuentes = new FuentesKpiFalsas
            {
                Stock =
                {
                    Fila("ALG", "1", "_RegInvent", 2, 0, 10m),
                    Fila("ALG", "2", "_RegInvent", 0, 3, 4m),
                    Fila("ALG", "3", "_ErrRepo", 0, 5, 100m),   // reposición: no es inventario
                    Fila("REI", "1", "_RegInvent", 0, 1, 10m)
                }
            };

            List<ValorKpi> valores = new KpiDiferenciasInventario().Calcular(SEMANA, fuentes);

            ValorKpi alg = valores.Single(v => v.Dueno == "ALG");
            Assert.AreEqual(32m, alg.Valor);       // 20 € que sobraban + 12 € que faltaban
            Assert.AreEqual(2m, alg.Numerador);    // productos descuadrados
            StringAssert.Contains(alg.Detalle, "faltaban 3 ud.");
            Assert.AreEqual(10m, valores.Single(v => v.Dueno == "REI").Valor);
            Assert.AreEqual(0m, valores.Single(v => v.Dueno == "ALC").Numerador);
        }

        [TestMethod]
        public void QuienPierde_ResolucionesDeSiempreYDesacuerdoPorUnidad()
        {
            var diferencias = new List<DiferenciaReposicionKpi>
            {
                new DiferenciaReposicionKpi { Id = 1, Origen = "ALG", Destino = "REI", Diferencia = -2, Resolucion = "NoEstaba", PrecioMedio = 5m },       // origen
                new DiferenciaReposicionKpi { Id = 2, Origen = "ALG", Destino = "REI", Diferencia = 1, Resolucion = "ErrorRecepcion", PrecioMedio = 3m },  // destino
                new DiferenciaReposicionKpi { Id = 3, Origen = "ALG", Destino = "ALC", Diferencia = -3, Resolucion = "Desacuerdo", PrecioMedio = 10m },
                new DiferenciaReposicionKpi { Id = 4, Origen = "ALG", Destino = "ALC", Diferencia = -4, Estado = "Pendiente", PrecioMedio = 10m }
            };
            var imputaciones = new List<ImputacionReposicionKpi>
            {
                new ImputacionReposicionKpi { DiferenciaId = 3, Almacen = "ALC", Unidades = 2 },
                new ImputacionReposicionKpi { DiferenciaId = 3, Almacen = "alg", Unidades = 1 }
            };

            ResultadoQuienPierdeReposicion r = ReglaQuienPierdeReposicion.Imputar(diferencias, imputaciones);

            Assert.AreEqual(3, r.Imputadas.Where(i => i.Almacen == "ALG").Sum(i => i.Unidades)); // 2 NoEstaba + 1 del desacuerdo
            Assert.AreEqual(1, r.Imputadas.Where(i => i.Almacen == "REI").Sum(i => i.Unidades));
            Assert.AreEqual(2, r.Imputadas.Where(i => i.Almacen == "ALC").Sum(i => i.Unidades));
            Assert.AreEqual(20m, r.Imputadas.Where(i => i.Almacen == "ALC").Sum(i => i.Valor));
            Assert.AreEqual(4, r.UnidadesSinImputar);
            Assert.AreEqual(1, r.DiferenciasSinImputar);
        }

        [TestMethod]
        public void QuienPierde_DesacuerdoSinImputacionesNiOrigen_SinImputar()
        {
            var diferencias = new List<DiferenciaReposicionKpi>
            {
                new DiferenciaReposicionKpi { Id = 5, Origen = "ALG", Destino = "REI", Diferencia = -1, Resolucion = "Desacuerdo" },
                new DiferenciaReposicionKpi { Id = 6, Origen = "", Destino = "REI", Diferencia = -1, Resolucion = "Confirmada" }
            };

            ResultadoQuienPierdeReposicion r = ReglaQuienPierdeReposicion.Imputar(diferencias, null);

            Assert.AreEqual(0, r.Imputadas.Count);
            Assert.AreEqual(2, r.UnidadesSinImputar);
        }

        [TestMethod]
        public void DiferenciasReposicion_PorAlmacenConLasPendientesEnElDetalle()
        {
            var fuentes = new FuentesKpiFalsas
            {
                Reposicion = new LecturaDiferenciasReposicionKpi
                {
                    HayDiferencias = true,
                    Diferencias =
                    {
                        new DiferenciaReposicionKpi { Id = 1, Origen = "REI", Destino = "ALG", Diferencia = -2, Resolucion = "Confirmada", PrecioMedio = 7.5m },
                        new DiferenciaReposicionKpi { Id = 2, Origen = "ALG", Destino = "ALC", Diferencia = -1, Estado = "Pendiente", PrecioMedio = 1m }
                    }
                }
            };

            List<ValorKpi> valores = new KpiDiferenciasReposicion().Calcular(SEMANA, fuentes);

            ValorKpi rei = valores.Single(v => v.Dueno == "REI");
            Assert.AreEqual(15m, rei.Valor);
            Assert.AreEqual(2m, rei.Numerador);
            StringAssert.Contains(rei.Detalle, "1 ud. de 1 diferencias sin resolver");
            Assert.AreEqual(0m, valores.Single(v => v.Dueno == "ALG").Valor);
        }

        [TestMethod]
        public void DiferenciasReposicion_SinLaTabla_Nada()
        {
            var fuentes = new FuentesKpiFalsas { Reposicion = new LecturaDiferenciasReposicionKpi { HayDiferencias = false } };

            Assert.AreEqual(0, new KpiDiferenciasReposicion().Calcular(SEMANA, fuentes).Count);
        }

        [TestMethod]
        public void EntregasIncompletas_ServidosEsaSemana_EmpresaYAlmacenDeSalida()
        {
            HechosPedidoPerfecto H(int n, DateTime fecha, DateTime? albaran, string almacen, bool nota = false, bool reparto = false) =>
                new HechosPedidoPerfecto { Pedido = n, Fecha = fecha, PrimerAlbaran = albaran, Almacen = almacen, TieneNotaEntregaPendiente = nota, EntregaIncompleta = reparto };
            var fuentes = new FuentesKpiFalsas();
            foreach (HechosPedidoPerfecto h in new[]
            {
                H(1, LUNES.AddDays(-20), LUNES.AddDays(1), "ALG", nota: true),   // pedido de hace semanas, servido esta: cuenta
                H(2, LUNES, LUNES.AddDays(2), "ALG", reparto: true),
                H(3, LUNES, LUNES.AddDays(2), null),                               // sin almacén: Algete
                H(4, LUNES.AddDays(1), LUNES.AddDays(3), "REI"),
                H(5, LUNES.AddDays(-3), LUNES.AddDays(-1), "ALG", nota: true),   // servido la semana anterior: no cuenta
                H(6, LUNES.AddDays(2), null, "ALG"),                               // sin servir: no cuenta
                H(7, LUNES.AddDays(-40), LUNES.AddDays(2), "ALG", nota: true)    // más de 28 días antes: se escapa
            })
            {
                fuentes.Pedidos.Add(new KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>(h, new EvaluacionPedidoPerfecto()));
            }

            List<ValorKpi> valores = new KpiEntregasIncompletas().Calcular(SEMANA, fuentes);

            ValorKpi empresa = valores.Single(v => v.TipoDueno == TipoDuenoKpi.Empresa);
            Assert.AreEqual("1", empresa.Dueno);
            Assert.AreEqual(2m, empresa.Numerador);
            Assert.AreEqual(4m, empresa.Denominador);
            Assert.AreEqual(50m, empresa.Valor);
            ValorKpi alg = valores.Single(v => v.TipoDueno == TipoDuenoKpi.Almacen && v.Dueno == "ALG");
            Assert.AreEqual(3m, alg.Denominador);
            Assert.AreEqual(66.7m, alg.Valor);
            Assert.AreEqual(0m, valores.Single(v => v.Dueno == "REI").Valor);
            StringAssert.Contains(empresa.Detalle, "1 con nota de entrega");
            CollectionAssert.AreEqual(new[] { "pedidos 19/10-23/11" }, fuentes.Llamadas);
        }

        [TestMethod]
        public void PedidoPerfecto_PorVendedorSinContarLosInformativos_yElMotivoQueMasResta()
        {
            var fuentes = new FuentesKpiFalsas
            {
                Pedidos =
                {
                    Pedido(1, LUNES, "je"),
                    Pedido(2, LUNES.AddDays(1), "JE", MotivoPedidoPerfecto.NoCobradoAlContado),           // informativo: sigue siendo perfecto
                    Pedido(3, LUNES.AddDays(2), "JE", MotivoPedidoPerfecto.FaltabaStock, MotivoPedidoPerfecto.NoSalioAlDiaSiguiente),
                    Pedido(4, LUNES.AddDays(3), "JE", MotivoPedidoPerfecto.NoSalioAlDiaSiguiente),
                    Pedido(5, LUNES.AddDays(4), "AH", MotivoPedidoPerfecto.Impagado),
                    Pedido(6, LUNES.AddDays(5), ""),                                                        // sin vendedor: solo en la empresa
                    Pedido(7, LUNES.AddDays(7), "JE")                                                       // la semana siguiente
                }
            };

            List<ValorKpi> valores = new KpiPedidoPerfecto().Calcular(SEMANA, fuentes);

            ValorKpi je = valores.Single(v => v.TipoDueno == TipoDuenoKpi.Vendedor && v.Dueno == "JE");
            Assert.AreEqual(2m, je.Numerador);
            Assert.AreEqual(4m, je.Denominador);
            Assert.AreEqual(50m, je.Valor);
            StringAssert.StartsWith(je.Detalle, "Lo que más resta: NoSalioAlDiaSiguiente (2 de 4 pedidos)");
            ValorKpi empresa = valores.Single(v => v.TipoDueno == TipoDuenoKpi.Empresa);
            Assert.AreEqual(6m, empresa.Denominador);
            Assert.AreEqual(3m, empresa.Numerador);
            Assert.AreEqual(0m, valores.Single(v => v.Dueno == "AH").Valor);
            Assert.AreEqual(3, valores.Count);
        }

        [TestMethod]
        public void PedidoPerfecto_MaduraLoQueElPlazoMasLargoDeLaRegla()
        {
            Assert.AreEqual(ReglaPedidoPerfecto.DIAS_PLAZO_SIN_SERVIR, new KpiPedidoPerfecto().DiasMaduracion);
        }

        [TestMethod]
        public void ReenviadoSinCoste_SinFuente_Vacio()
        {
            var kpi = new KpiReenviadoSinCoste();

            Assert.IsFalse(kpi.TieneFuente);
            Assert.AreEqual(0, kpi.Calcular(SEMANA, new FuentesKpiFalsas()).Count);
        }

        [TestMethod]
        public void Lectores_ConResultadosSimulados()
        {
            var stock = new DataTable();
            foreach (string c in new[] { "Almacen", "Mes", "Producto", "Diario", "Altas", "Bajas", "Nombre", "Familia", "PrecioMedio" })
            {
                _ = stock.Columns.Add(c, c == "Mes" || c == "Altas" || c == "Bajas" ? typeof(int) : c == "PrecioMedio" ? typeof(decimal) : typeof(string));
            }
            _ = stock.Rows.Add("ALG ", 11, "17404 ", "_RegInvent", 0, 3, "Champú", "Eva", DBNull.Value);
            var diferencias = new DataTable();
            foreach (string c in new[] { "Id", "Origen", "Destino", "Producto", "Diferencia", "Estado", "Resolucion", "PrecioMedio" })
            {
                _ = diferencias.Columns.Add(c, c == "Id" || c == "Diferencia" ? typeof(int) : c == "PrecioMedio" ? typeof(decimal) : typeof(string));
            }
            _ = diferencias.Rows.Add(9, "ALG", "REI", "40372", -1, "Resuelta", "NoEstaba", 4.25m);
            var imputaciones = new DataTable();
            _ = imputaciones.Columns.Add("DiferenciaId", typeof(int));
            _ = imputaciones.Columns.Add("Almacen", typeof(string));
            _ = imputaciones.Columns.Add("Unidades", typeof(int));
            _ = imputaciones.Rows.Add(9, "REI", 1);

            FilaPerdidaDesconocidaDTO f = FuentesKpiSql.LeerFilasStock(stock.CreateDataReader()).Single();
            DiferenciaReposicionKpi d = FuentesKpiSql.LeerDiferencias(diferencias.CreateDataReader()).Single();
            ImputacionReposicionKpi i = FuentesKpiSql.LeerImputaciones(imputaciones.CreateDataReader()).Single();

            Assert.AreEqual("ALG", f.Almacen);
            Assert.AreEqual("17404", f.Producto);
            Assert.IsNull(f.PrecioMedio);
            Assert.AreEqual(3, f.Bajas);
            Assert.AreEqual(-1, d.Diferencia);
            Assert.AreEqual(4.25m, d.PrecioMedio);
            Assert.AreEqual("NoEstaba", d.Resolucion);
            Assert.AreEqual("REI", i.Almacen);
        }

        [TestMethod]
        public void Fuentes_PedidosPorMesesEnteros_YUnaLecturaPorMes()
        {
            var leidos = new List<string>();
            var fuentes = new FuentesKpiSql("sin conexión", (e, d, h) =>
            {
                leidos.Add($"{d:dd/MM}-{h:dd/MM}");
                return new CalculoPedidoPerfecto
                {
                    Pedidos = new List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>>
                    {
                        new KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>(new HechosPedidoPerfecto { Pedido = d.Month, Fecha = d.AddDays(27) }, new EvaluacionPedidoPerfecto())
                    }
                };
            });

            var semana1 = fuentes.PedidosPerfecto(new DateTime(2026, 10, 26), new DateTime(2026, 11, 2)); // 28/10 sí
            var semana2 = fuentes.PedidosPerfecto(new DateTime(2026, 11, 23), new DateTime(2026, 11, 30)); // 28/11 sí

            CollectionAssert.AreEqual(new[] { "01/10-01/11", "01/11-01/12" }, leidos);
            Assert.AreEqual(10, semana1.Single().Key.Pedido);
            Assert.AreEqual(11, semana2.Single().Key.Pedido);
            _ = fuentes.PedidosPerfecto(new DateTime(2026, 10, 1), new DateTime(2026, 12, 1));
            Assert.AreEqual(2, leidos.Count); // ya leídos: no vuelve a la BD
        }
    }
}
