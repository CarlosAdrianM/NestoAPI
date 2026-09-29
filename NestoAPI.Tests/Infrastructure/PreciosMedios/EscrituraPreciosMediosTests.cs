using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreciosMedios;

namespace NestoAPI.Tests.Infrastructure.PreciosMedios
{
    /// <summary>
    /// Issue #547, corte (c): qué escribe el incremental (planificador), con qué SQL (texto y parámetros, sin
    /// ejecutar nada contra la BD) y el escritor como único punto de escritura.
    /// </summary>
    [TestClass]
    public class EscrituraPreciosMediosTests
    {
        private static readonly DateTime F1 = new DateTime(2025, 1, 10, 10, 0, 0);
        private static readonly DateTime F2 = new DateTime(2025, 2, 10, 12, 30, 0);
        private static readonly DateTime F3 = new DateTime(2025, 3, 10, 9, 0, 0);
        private static readonly DateTime CORTE = new DateTime(2026, 9, 27, 0, 30, 0);

        private static readonly string[] FIXTURES =
        {
            "45934", "38896", "41281", "44904", "34248", "40985", "45396", "17877", "16137",
            "45188", "44243", "43859", "45039", "34929", "37688", "40179", "45668", "47074"
        };

        #region Planificador

        [TestMethod]
        public void Planificar_LasFixturesTalComoLasDejoElSP_NoEscribeNiProductosNiCompras()
        {
            // Paridad: si el incremental pasa por un producto que el SP acaba de dejar bien, no cambia nada.
            List<string> fallos = new List<string>();
            foreach (string producto in FIXTURES)
            {
                FixturePrecioMedio fixture = FixturePrecioMedio.Cargar(producto);
                PlanEscrituraPrecioMedio plan = EscritorPreciosMedios.Planificar(ServicioSombraPreciosMediosTests.DatosDesdeFixture(fixture));
                if (fixture.MotivoEsperado != MotivoPrecioMedioNoProcesado.Ninguno)
                {
                    if (plan.Procesado || SqlEscrituraPreciosMedios.Generar(plan).Any())
                    {
                        fallos.Add(producto + ": no procesado según el SP, pero el plan escribe");
                    }
                    continue;
                }
                if (plan.PrecioMedioNuevo.HasValue)
                {
                    fallos.Add(producto + ": cambiaría la media a " + plan.PrecioMedioNuevo);
                }
                if (plan.CostesCompra.Any())
                {
                    fallos.Add(producto + ": cambiaría " + plan.CostesCompra.Count + " costes de compra");
                }
                if (plan.CosteVentasPendientes != fixture.PrecioMedioSP)
                {
                    fallos.Add(producto + ": pendientes a " + plan.CosteVentasPendientes + " en vez de la media " + fixture.PrecioMedioSP);
                }
            }
            Assert.AreEqual(0, fallos.Count, string.Join("; ", fallos));
        }

        [TestMethod]
        public void Planificar_45396ConLaCompraDelLunesFacturada_EscribeLaMedia5_2478EnFichaYLinea()
        {
            DatosProductoPrecioMedio datos = ServicioSombraPreciosMediosTests.DatosDesdeFixture(FixturePrecioMedio.Cargar("45396"));
            LineaCompraBDPrecioMedio compraDelLunes = datos.Compras.Single(l => l.NumeroOrden == 141491851);
            DateTime fecha = new DateTime(2026, 9, 28, 8, 42, 22, 423);
            compraDelLunes.Estado = 4;
            compraDelLunes.NumeroFactura = 88491;
            compraDelLunes.NumeroAlbaran = 125412;
            compraDelLunes.FechaAlbaran = fecha;
            int stockAntes = ConstructorHistorialPrecioMedio.CrearStockHasta(datos.Movimientos)(fecha);
            datos.Movimientos.Add(new MovimientoStockPrecioMedio { Fecha = fecha, Cantidad = 37 - stockAntes, EsRecepcion = true });

            PlanEscrituraPrecioMedio plan = EscritorPreciosMedios.Planificar(datos);

            Assert.IsTrue(plan.Procesado);
            Assert.AreEqual(5.2478m, plan.PrecioMedioNuevo);
            Assert.AreEqual(1, plan.CostesCompra.Count, "solo cambia la línea recién facturada");
            Assert.AreEqual(5.2478m, plan.CostesCompra[141491851]);
            Assert.AreEqual(5.2478m, plan.CosteVentasPendientes);
            RangoCosteVenta ultimo = plan.RangosVenta.Last();
            Assert.AreEqual(fecha, ultimo.Desde);
            Assert.AreEqual(CalculadoraPrecioMedio.FechaFinDeLosTiempos, ultimo.Hasta);
            Assert.AreEqual(5.2478m, ultimo.Coste);
            Assert.AreEqual(5.25m, plan.RangosVenta[plan.RangosVenta.Count - 2].Coste, "las ventas anteriores a la compra conservan 5,25");
        }

        [TestMethod]
        public void Planificar_FacturaNueva_SoloCambiaLaFichaYLaLineaNueva()
        {
            // Compra 1: 10 uds a 100 € (media 10, ya escrita). Compra 2: 10 uds a 200 € con stock 20 → (10×10+200)/20 = 15.
            PlanEscrituraPrecioMedio plan = EscritorPreciosMedios.Planificar(ServicioSombraPreciosMediosTests.ProductoConFacturaPosterior());

            Assert.AreEqual(15m, plan.PrecioMedioNuevo);
            CollectionAssert.AreEquivalent(new[] { 2 }, plan.CostesCompra.Keys.ToList());
            Assert.AreEqual(15m, plan.CostesCompra[2]);
            Assert.AreEqual(2, plan.RangosVenta.Count);
            Assert.AreEqual(CalculadoraPrecioMedio.FechaPrincipioDeLosTiempos, plan.RangosVenta[0].Desde);
            Assert.AreEqual(F2, plan.RangosVenta[0].Hasta);
            Assert.AreEqual(10m, plan.RangosVenta[0].Coste);
            Assert.AreEqual(F2, plan.RangosVenta[1].Desde);
            Assert.AreEqual(15m, plan.RangosVenta[1].Coste);
            Assert.AreEqual(15m, plan.CosteVentasPendientes);
        }

        [TestMethod]
        public void Planificar_EmpateDeFacturasALaMismaHora_GanaSiempreLaUltima()
        {
            // Decisión de Carlos: ante empates, siempre la línea más reciente en el orden del C# (la factura mayor).
            PlanEscrituraPrecioMedio plan = EscritorPreciosMedios.Planificar(ServicioSombraPreciosMediosTests.ProductoConEmpate());

            Assert.AreEqual(20m, plan.PrecioMedioNuevo);
            Assert.AreEqual(1, plan.RangosVenta.Count, "los dos tramos solapados se resuelven en un solo rango");
            Assert.AreEqual(20m, plan.RangosVenta[0].Coste);
            Assert.AreEqual(20m, plan.CosteVentasPendientes);
        }

        [TestMethod]
        public void Planificar_ProductoExcluidoPorMontaje_NoEscribeNada()
        {
            PlanEscrituraPrecioMedio plan = EscritorPreciosMedios.Planificar(
                ServicioSombraPreciosMediosTests.DatosDesdeFixture(FixturePrecioMedio.Cargar("34929")));

            Assert.IsFalse(plan.Procesado);
            Assert.AreEqual(MotivoPrecioMedioNoProcesado.ExcluidoPorMontaje, plan.Motivo);
            Assert.IsFalse(plan.HayQueEscribir);
            Assert.AreEqual(0, SqlEscrituraPreciosMedios.Generar(plan).Count);
        }

        [TestMethod]
        public void Planificar_SinFichaEnLaEmpresa_NoEscribeNada()
        {
            DatosProductoPrecioMedio datos = ServicioSombraPreciosMediosTests.ProductoIgual();
            datos.Ficha = null;

            PlanEscrituraPrecioMedio plan = EscritorPreciosMedios.Planificar(datos);

            Assert.IsFalse(plan.Procesado);
            Assert.AreEqual(0, SqlEscrituraPreciosMedios.Generar(plan).Count);
        }

        [TestMethod]
        public void RangosEfectivos_SolapesHuecosYContiguos()
        {
            List<TramoPrecioMedio> tramos = new List<TramoPrecioMedio>
            {
                new TramoPrecioMedio { FechaDesde = CalculadoraPrecioMedio.FechaPrincipioDeLosTiempos, FechaHasta = F2, PrecioMedio = 10m },
                new TramoPrecioMedio { FechaDesde = F1, FechaHasta = F2, PrecioMedio = 12m },   // solapa: gana (último)
                new TramoPrecioMedio { FechaDesde = F2, FechaHasta = F3, PrecioMedio = 12m },   // contiguo con el mismo coste: se junta
                new TramoPrecioMedio { FechaDesde = F3.AddDays(1), FechaHasta = CalculadoraPrecioMedio.FechaFinDeLosTiempos, PrecioMedio = 14m } // hueco antes
            };

            List<RangoCosteVenta> rangos = PlanificadorEscrituraPreciosMedios.RangosEfectivos(tramos);

            Assert.AreEqual(3, rangos.Count, string.Join(" ", rangos));
            Assert.AreEqual(10m, rangos[0].Coste);
            Assert.AreEqual(F1, rangos[0].Hasta);
            Assert.AreEqual(F1, rangos[1].Desde);
            Assert.AreEqual(F3, rangos[1].Hasta);
            Assert.AreEqual(12m, rangos[1].Coste);
            Assert.AreEqual(F3.AddDays(1), rangos[2].Desde, "el hueco [F3, F3+1) no se escribe: el SP no casa esas ventas");
        }

        [TestMethod]
        public void RangosEfectivos_CoincidenConLoQueElComparadorDaPorBueno_EnTodasLasFixtures()
        {
            // El comparador de la sombra acepta en cada venta el precio de su último tramo; el escritor debe escribir ese.
            foreach (string producto in FIXTURES)
            {
                ResultadoPrecioMedio calculo = CalculadoraPrecioMedio.Calcular(FixturePrecioMedio.Cargar(producto).CrearHistorial());
                List<RangoCosteVenta> rangos = PlanificadorEscrituraPreciosMedios.RangosEfectivos(calculo.Tramos);
                IEnumerable<DateTime> fechas = calculo.Tramos.SelectMany(t => new[] { t.FechaDesde, t.FechaDesde.AddMilliseconds(3), t.FechaHasta.AddMilliseconds(-3) });
                foreach (DateTime fecha in fechas.Where(f => f < CalculadoraPrecioMedio.FechaFinDeLosTiempos))
                {
                    List<decimal> candidatos = ComparadorSombraPreciosMedios.PreciosDeLosTramos(calculo.Tramos,
                        new LineaVentaPrecioMedio { Estado = 2, FechaAlbaran = fecha });
                    RangoCosteVenta rango = rangos.SingleOrDefault(r => fecha >= r.Desde && fecha < r.Hasta);
                    if (candidatos.Count == 0)
                    {
                        Assert.IsNull(rango, producto + " " + fecha);
                    }
                    else
                    {
                        Assert.IsNotNull(rango, producto + " " + fecha);
                        Assert.AreEqual(candidatos.Last(), rango.Coste, producto + " " + fecha);
                    }
                }
            }
        }

        #endregion

        #region SQL generado

        private static PlanEscrituraPrecioMedio PlanCompleto()
        {
            PlanEscrituraPrecioMedio plan = new PlanEscrituraPrecioMedio
            {
                Empresa = "1",
                EmpresaEspejo = "3",
                Producto = "41281",
                Procesado = true,
                PrecioMedioNuevo = 32.1662m,
                CosteVentasPendientes = 32.1662m
            };
            plan.CostesCompra[101] = 30m;
            plan.CostesCompra[102] = 32.1662m;
            plan.CostesCompra[103] = 32.1662m;
            plan.RangosVenta.Add(new RangoCosteVenta { Desde = CalculadoraPrecioMedio.FechaPrincipioDeLosTiempos, Hasta = F2, Coste = 30m });
            plan.RangosVenta.Add(new RangoCosteVenta { Desde = F2, Hasta = CalculadoraPrecioMedio.FechaFinDeLosTiempos, Coste = 32.1662m });
            return plan;
        }

        [TestMethod]
        public void Generar_TodosLosUpdateLlevanSoloLoQueCambiaYVanAcotadosPorProducto()
        {
            List<ComandoEscrituraPrecioMedio> comandos = SqlEscrituraPreciosMedios.Generar(PlanCompleto());

            Assert.AreEqual(5, comandos.Count, "ficha + 2 costes de compra + ventas por fecha + pendientes");
            foreach (ComandoEscrituraPrecioMedio c in comandos)
            {
                StringAssert.Contains(c.Texto, "UPDATE ");
                StringAssert.Contains(c.Texto, "EXCEPT SELECT", "predicado «solo lo que cambia» del paso 1");
                StringAssert.Contains(c.Texto, "@producto");
                Assert.IsFalse(c.Texto.Contains("NOLOCK"));
                Assert.IsFalse(c.Texto.IndexOf("INDEX", StringComparison.OrdinalIgnoreCase) >= 0, "nada de índices (#542)");
                Assert.AreEqual("41281", c.Parametros.Single(p => p.Nombre == "@producto").Valor);
                foreach (string nombre in c.Parametros.Select(p => p.Nombre))
                {
                    StringAssert.Contains(c.Texto, nombre, "parámetro sin usar: " + nombre);
                }
            }
        }

        [TestMethod]
        public void Generar_Productos_SoloEmpresaE_NuncaLaEspejo()
        {
            ComandoEscrituraPrecioMedio c = SqlEscrituraPreciosMedios.Generar(PlanCompleto()).Single(x => x.Tabla == TablaEscrituraPrecioMedio.Productos);

            StringAssert.Contains(c.Texto, "UPDATE Productos SET PrecioMedio = @precioMedio");
            StringAssert.Contains(c.Texto, "WHERE Empresa = @empresa AND Número = @producto");
            StringAssert.Contains(c.Texto, "EXISTS (SELECT PrecioMedio EXCEPT SELECT @precioMedio)");
            Assert.IsFalse(c.Texto.Contains("@espejo"));
            Assert.AreEqual("1", c.Parametros.Single(p => p.Nombre == "@empresa").Valor);
            Assert.AreEqual(32.1662m, c.Parametros.Single(p => p.Nombre == "@precioMedio").Valor);
            Assert.AreEqual(System.Data.SqlDbType.Money, c.Parametros.Single(p => p.Nombre == "@precioMedio").Tipo);
            Assert.IsFalse(c.EnLotes);
        }

        [TestMethod]
        public void Generar_Compras_UnaSentenciaPorCoste_EmpresaYEspejo()
        {
            List<ComandoEscrituraPrecioMedio> compras = SqlEscrituraPreciosMedios.Generar(PlanCompleto())
                .Where(x => x.Tabla == TablaEscrituraPrecioMedio.LinPedidoCmp).ToList();

            Assert.AreEqual(2, compras.Count);
            ComandoEscrituraPrecioMedio dos = compras.Single(c => (decimal)c.Parametros.Single(p => p.Nombre == "@coste").Valor == 32.1662m);
            StringAssert.Contains(dos.Texto, "NºOrden IN (@o0, @o1)");
            StringAssert.Contains(dos.Texto, "Empresa IN (@empresa, @espejo)");
            StringAssert.Contains(dos.Texto, "EXISTS (SELECT Coste EXCEPT SELECT @coste)");
            CollectionAssert.AreEqual(new object[] { 102, 103 }, dos.Parametros.Where(p => p.Nombre.StartsWith("@o")).Select(p => p.Valor).ToList());
            Assert.AreEqual("3", dos.Parametros.Single(p => p.Nombre == "@espejo").Valor);
        }

        [TestMethod]
        public void Generar_Compras_MuchasLineas_SeTrocean()
        {
            PlanEscrituraPrecioMedio plan = PlanCompleto();
            plan.CostesCompra.Clear();
            for (int i = 0; i < SqlEscrituraPreciosMedios.LINEAS_POR_COMANDO * 2 + 1; i++)
            {
                plan.CostesCompra[1000 + i] = 5m;
            }

            List<ComandoEscrituraPrecioMedio> compras = SqlEscrituraPreciosMedios.Generar(plan)
                .Where(x => x.Tabla == TablaEscrituraPrecioMedio.LinPedidoCmp).ToList();

            Assert.AreEqual(3, compras.Count);
            Assert.AreEqual(SqlEscrituraPreciosMedios.LINEAS_POR_COMANDO, compras[0].Parametros.Count(p => p.Nombre.StartsWith("@o")));
            Assert.AreEqual(1, compras[2].Parametros.Count(p => p.Nombre.StartsWith("@o")));
        }

        [TestMethod]
        public void Generar_VentasPorFecha_TablaDeValoresEnLotesConElPredicadoDelPaso1()
        {
            ComandoEscrituraPrecioMedio c = SqlEscrituraPreciosMedios.Generar(PlanCompleto())
                .Single(x => x.Tabla == TablaEscrituraPrecioMedio.LinPedidoVta && x.Texto.Contains("VALUES"));

            Assert.IsTrue(c.EnLotes);
            StringAssert.Contains(c.Texto, "UPDATE TOP (@lote) v SET Coste = t.Coste");
            StringAssert.Contains(c.Texto, "JOIN (VALUES (@d0, @h0, @c0), (@d1, @h1, @c1)) AS t (Desde, Hasta, Coste)");
            StringAssert.Contains(c.Texto, "ON v.[Fecha Albarán] >= t.Desde AND v.[Fecha Albarán] < t.Hasta");
            StringAssert.Contains(c.Texto, "WHERE v.Producto = @producto AND v.Estado >= 2 AND v.Empresa IN (@empresa, @espejo)");
            StringAssert.Contains(c.Texto, "AND EXISTS (SELECT v.Coste EXCEPT SELECT t.Coste)");
            Assert.AreEqual(SqlEscrituraPreciosMedios.TAMANO_LOTE_VENTAS, c.Parametros.Single(p => p.Nombre == "@lote").Valor);
            Assert.IsTrue(SqlEscrituraPreciosMedios.TAMANO_LOTE_VENTAS < 5000, "por debajo del escalado a bloqueo de tabla");
            Assert.AreEqual(CalculadoraPrecioMedio.FechaPrincipioDeLosTiempos, c.Parametros.Single(p => p.Nombre == "@d0").Valor);
            Assert.AreEqual(F2, c.Parametros.Single(p => p.Nombre == "@h0").Valor);
            Assert.AreEqual(30m, c.Parametros.Single(p => p.Nombre == "@c0").Valor);
            Assert.AreEqual(CalculadoraPrecioMedio.FechaFinDeLosTiempos, c.Parametros.Single(p => p.Nombre == "@h1").Valor);
        }

        [TestMethod]
        public void Generar_VentasPorFecha_MuchosRangos_SeTroceanPorDebajoDelLimiteDeParametros()
        {
            PlanEscrituraPrecioMedio plan = PlanCompleto();
            plan.RangosVenta.Clear();
            for (int i = 0; i < SqlEscrituraPreciosMedios.RANGOS_POR_COMANDO + 1; i++)
            {
                plan.RangosVenta.Add(new RangoCosteVenta { Desde = F1.AddDays(i), Hasta = F1.AddDays(i + 1), Coste = i });
            }

            List<ComandoEscrituraPrecioMedio> ventas = SqlEscrituraPreciosMedios.Generar(plan)
                .Where(x => x.Tabla == TablaEscrituraPrecioMedio.LinPedidoVta && x.Texto.Contains("VALUES")).ToList();

            Assert.AreEqual(2, ventas.Count);
            Assert.IsTrue(ventas.All(v => v.Parametros.Count < 2100));
        }

        [TestMethod]
        public void Generar_VentasPendientes_EstadosMenosUnoYUno_ConElUltimoTramo()
        {
            ComandoEscrituraPrecioMedio c = SqlEscrituraPreciosMedios.Generar(PlanCompleto())
                .Single(x => x.Tabla == TablaEscrituraPrecioMedio.LinPedidoVta && !x.Texto.Contains("VALUES"));

            Assert.IsTrue(c.EnLotes);
            StringAssert.Contains(c.Texto, "UPDATE TOP (@lote) LinPedidoVta SET Coste = @coste");
            StringAssert.Contains(c.Texto, "Estado IN (-1, 1)");
            StringAssert.Contains(c.Texto, "EXISTS (SELECT Coste EXCEPT SELECT @coste)");
            Assert.AreEqual(32.1662m, c.Parametros.Single(p => p.Nombre == "@coste").Valor);
        }

        [TestMethod]
        public void Generar_SinEspejo_UsaLaPropiaEmpresa()
        {
            PlanEscrituraPrecioMedio plan = PlanCompleto();
            plan.Empresa = "4";
            plan.EmpresaEspejo = null;

            foreach (ComandoEscrituraPrecioMedio c in SqlEscrituraPreciosMedios.Generar(plan).Where(x => x.Tabla != TablaEscrituraPrecioMedio.Productos))
            {
                Assert.AreEqual("4", c.Parametros.Single(p => p.Nombre == "@espejo").Valor);
            }
        }

        [TestMethod]
        public void Generar_PlanSinCambiosDeFichaNiCompras_SoloMandaLasVentas()
        {
            PlanEscrituraPrecioMedio plan = PlanCompleto();
            plan.PrecioMedioNuevo = null;
            plan.CostesCompra.Clear();

            List<ComandoEscrituraPrecioMedio> comandos = SqlEscrituraPreciosMedios.Generar(plan);

            Assert.IsTrue(comandos.All(c => c.Tabla == TablaEscrituraPrecioMedio.LinPedidoVta));
            Assert.AreEqual(2, comandos.Count);
        }

        [TestMethod]
        public void LecturaDelEscritor_MismasConsultasQueLaSombraPeroSinNolock()
        {
            foreach (string sql in new[] { RepositorioPreciosMediosSql.SQL_FICHA, RepositorioPreciosMediosSql.SQL_COMPRAS,
                RepositorioPreciosMediosSql.SQL_MOVIMIENTOS, RepositorioPreciosMediosSql.SQL_VENTAS })
            {
                StringAssert.Contains(sql, "WITH (NOLOCK)");
                string sinNolock = RepositorioPreciosMediosSql.SinNolock(sql);
                Assert.IsFalse(sinNolock.Contains("NOLOCK"), sinNolock);
                StringAssert.Contains(sinNolock, "@producto");
            }
        }

        [TestMethod]
        public void Seleccion_RiesgoDos_ApuntesGrabadosDesdeLaMarcaConFechaEnOAntesDeUnaCompraFacturada()
        {
            string sql = RepositorioEscrituraPreciosMediosSql.SQL_PRODUCTOS_MOVIMIENTOS_FECHA_PASADA;

            StringAssert.Contains(sql, "FROM ExtractoProducto e WITH (NOLOCK)");
            StringAssert.Contains(sql, "e.Empresa IN (@empresa, @espejo) AND e.[Nº Orden] > @desdeNumOrden AND e.[Nº Orden] <= @hastaNumOrden",
                "búsqueda por la clave primaria (Empresa, Nº Orden)");
            Assert.IsFalse(sql.Contains("Fecha Modificación"), "ExtractoProducto no tiene índice por Fecha Modificación: recorrería la tabla");
            StringAssert.Contains(sql, "l.Estado = 4 AND l.FechaAlbarán >= e.Fecha", "el stock del SP es Fecha <= FechaAlbarán");
            StringAssert.Contains(sql, "l.Producto = e.Número");
        }

        [TestMethod]
        public void Seleccion_ComprasModificadas_LineasYFacturas()
        {
            string sql = RepositorioEscrituraPreciosMediosSql.SQL_PRODUCTOS_COMPRAS_MODIFICADAS;

            StringAssert.Contains(sql, "l.[Fecha Modificación] >= @desde");
            StringAssert.Contains(sql, "c.[Fecha Modificación] >= @desde");
            StringAssert.Contains(sql, "l.TipoLínea = '1'");
            StringAssert.Contains(sql, "UNION");
        }

        [TestMethod]
        public void Seleccion_ComprasModificadas_TambienVeLasFacturasDeshechas()
        {
            // Corte (b): prdDeshacerFacturaCmp BORRA CabFacturaCmp y deja las líneas en estado 2 sin NºFactura, poniendo
            // [Fecha Modificación] (Scripts/Issue547_DeshacerFacturaCmp_FechaModificacion.sql). La rama de las LÍNEAS no
            // puede filtrar por estado ni por factura, o dejaría fuera justo esas líneas.
            string sql = RepositorioEscrituraPreciosMediosSql.SQL_PRODUCTOS_COMPRAS_MODIFICADAS;
            string ramaLineas = sql.Substring(0, sql.IndexOf("UNION", StringComparison.Ordinal));

            StringAssert.Contains(ramaLineas, "FROM LinPedidoCmp l");
            StringAssert.Contains(ramaLineas, "l.[Fecha Modificación] >= @desde");
            Assert.IsFalse(ramaLineas.Contains("Estado"), ramaLineas);
            Assert.IsFalse(ramaLineas.Contains("NºFactura"), ramaLineas);
            Assert.IsFalse(ramaLineas.Contains("CabFacturaCmp"), ramaLineas);
        }

        [TestMethod]
        public void NingunSqlDelIncremental_CreaIndicesNiTablas()
        {
            // #542: un índice filtrado en LinPedidoVta tumbó producción (módulos con QUOTED_IDENTIFIER OFF).
            foreach (string sql in new[]
            {
                SqlEscrituraPreciosMedios.SQL_PRODUCTO, SqlEscrituraPreciosMedios.SQL_VENTAS_PENDIENTES,
                RepositorioEscrituraPreciosMediosSql.SQL_SESION, RepositorioEscrituraPreciosMediosSql.SQL_APPLOCK,
                RepositorioEscrituraPreciosMediosSql.SQL_PRODUCTOS_COMPRAS_MODIFICADAS,
                RepositorioEscrituraPreciosMediosSql.SQL_PRODUCTOS_MOVIMIENTOS_FECHA_PASADA,
                RepositorioEscrituraPreciosMediosSql.SQL_PRODUCTOS_PEDIDO, RepositorioEscrituraPreciosMediosSql.SQL_LEER_ULTIMA_PASADA,
                RepositorioEscrituraPreciosMediosSql.SQL_GUARDAR_ULTIMA_PASADA, RepositorioEscrituraPreciosMediosSql.SQL_MAXIMO_NUM_ORDEN_EXTRACTO,
                RepositorioEscrituraPreciosMediosSql.SQL_PRODUCTOS_PENDIENTES, RepositorioEscrituraPreciosMediosSql.SQL_MARCAR_PENDIENTE,
                RepositorioEscrituraPreciosMediosSql.SQL_QUITAR_PENDIENTE
            })
            {
                Assert.IsFalse(sql.IndexOf("CREATE", StringComparison.OrdinalIgnoreCase) >= 0, sql);
                Assert.IsFalse(sql.IndexOf("INDEX", StringComparison.OrdinalIgnoreCase) >= 0, sql);
            }
        }

        [TestMethod]
        public void Sesion_PierdeLosInterbloqueosYNoEsperaIndefinidamente()
        {
            StringAssert.Contains(RepositorioEscrituraPreciosMediosSql.SQL_SESION, "SET DEADLOCK_PRIORITY LOW");
            StringAssert.Contains(RepositorioEscrituraPreciosMediosSql.SQL_SESION,
                "SET LOCK_TIMEOUT " + RepositorioEscrituraPreciosMediosSql.LOCK_TIMEOUT_MS + ";");
            StringAssert.Contains(RepositorioEscrituraPreciosMediosSql.SQL_APPLOCK, "@LockOwner = 'Transaction'");
            Assert.AreEqual("PreciosMedios#547:1:41281", RepositorioEscrituraPreciosMediosSql.RecursoBloqueo("1  ", " 41281 "));
        }

        [TestMethod]
        public void MarcaUltimaPasada_IdaYVuelta_YBasuraEsNula()
        {
            DateTime marca = new DateTime(2026, 9, 29, 2, 20, 5);
            string texto = RepositorioEscrituraPreciosMediosSql.FormatearMarca(marca);

            Assert.AreEqual("2026-09-29T02:20:05", texto);
            Assert.AreEqual(marca, RepositorioEscrituraPreciosMediosSql.InterpretarMarca(texto + "      "), "Valor es char(162): viene con relleno");
            Assert.IsNull(RepositorioEscrituraPreciosMediosSql.InterpretarMarca(null));
            Assert.IsNull(RepositorioEscrituraPreciosMediosSql.InterpretarMarca("ayer"));
        }

        [TestMethod]
        public void UltimoNumOrdenExtracto_IdaYVuelta_YBasuraEsNula()
        {
            string texto = RepositorioEscrituraPreciosMediosSql.FormatearNumOrden(4691450);

            Assert.AreEqual("4691450", texto);
            Assert.AreEqual(4691450, RepositorioEscrituraPreciosMediosSql.InterpretarNumOrden(texto + "      "), "Valor es char(162): viene con relleno");
            Assert.IsNull(RepositorioEscrituraPreciosMediosSql.InterpretarNumOrden(null));
            Assert.IsNull(RepositorioEscrituraPreciosMediosSql.InterpretarNumOrden("-5"));
            Assert.IsNull(RepositorioEscrituraPreciosMediosSql.InterpretarNumOrden("ayer"));
        }

        [TestMethod]
        public void MaximoNumOrdenExtracto_UnaBusquedaPorEmpresa()
        {
            string sql = RepositorioEscrituraPreciosMediosSql.SQL_MAXIMO_NUM_ORDEN_EXTRACTO;

            StringAssert.Contains(sql, "SELECT MAX([Nº Orden]) FROM ExtractoProducto WITH (NOLOCK) WHERE Empresa = @empresa");
            Assert.AreEqual("PreciosMediosUltimoNumOrdenExtracto", NestoAPI.Models.Constantes.ParametrosUsuario.PRECIOS_MEDIOS_ULTIMO_NUM_ORDEN_EXTRACTO);
        }

        #endregion

        #region Escritor

        [TestMethod]
        public void Escritor_DelegaEnElRepositorioConElPlanificadorPuro()
        {
            IRepositorioEscrituraPreciosMedios repositorio = A.Fake<IRepositorioEscrituraPreciosMedios>();
            DatosProductoPrecioMedio datos = ServicioSombraPreciosMediosTests.ProductoConFacturaPosterior();
            A.CallTo(() => repositorio.RecalcularYEscribir("1", "3", "PENDIENTE", A<Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio>>._))
                .ReturnsLazily((string e, string esp, string p, Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio> planificar) =>
                    new ResultadoEscrituraPrecioMedio { Empresa = e, Producto = p, Plan = planificar(datos) });

            ResultadoEscrituraPrecioMedio r = new EscritorPreciosMedios(repositorio).RecalcularProducto("1 ", "3", " PENDIENTE ");

            Assert.AreEqual(15m, r.Plan.PrecioMedioNuevo);
        }

        [TestMethod]
        public void Escritor_SinEspejo_UsaLaPropiaEmpresa()
        {
            IRepositorioEscrituraPreciosMedios repositorio = A.Fake<IRepositorioEscrituraPreciosMedios>();

            _ = new EscritorPreciosMedios(repositorio).RecalcularProducto("4", null, "37688");

            A.CallTo(() => repositorio.RecalcularYEscribir("4", "4", "37688", A<Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio>>._))
                .MustHaveHappenedOnceExactly();
        }

        #endregion

        [TestMethod]
        public void WebConfig_ElIncrementalNaceApagado()
        {
            // bin\Debug -> NestoAPI.Tests -> raíz de la solución -> NestoAPI\Web.config
            string ruta = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\NestoAPI\Web.config"));
            Assert.IsTrue(File.Exists(ruta), $"No se encuentra el Web.config en {ruta}");
            XmlDocument xml = new XmlDocument();
            xml.Load(ruta);
            XmlNode nodo = xml.SelectSingleNode("/configuration/appSettings/add[@key='" + PreciosMediosIncrementalJobsService.CLAVE_INTERRUPTOR + "']");

            Assert.IsNotNull(nodo, "Falta la clave del interruptor en Web.config");
            Assert.IsFalse(PreciosMediosIncrementalJobsService.EstaActivo(nodo.Attributes["value"].Value),
                "Se enciende a propósito cuando la sombra lleve dos domingos limpios, no antes");
        }
    }
}
