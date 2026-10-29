using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#532: criterio del recordatorio de reposición (ventas de todos los canales).
    /// </summary>
    [TestClass]
    public class CalculadoraReposicionTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 24);
        private static readonly ConsumiblesReposicion CONSUMIBLES = ConsumiblesReposicion.Leer(null);

        private Dictionary<string, ProductoReposicion> productos;
        private List<CompraDiaReposicion> compras;
        private List<LineaPendienteReposicion> pendientes;

        [TestInitialize]
        public void Setup()
        {
            productos = new Dictionary<string, ProductoReposicion>(StringComparer.OrdinalIgnoreCase)
            {
                ["CREMA500"] = Producto("CREMA500", "Crema hidratante 500 ml", "COS", "125", "Fama"),
                ["CREMA1000"] = Producto("CREMA1000", "Crema hidratante 1000 ml", "COS", "125", "Fama"),
                ["CREMAOTRA"] = Producto("CREMAOTRA", "Crema de otra marca", "COS", "125", "Lendan"),
                ["TINTE"] = Producto("TINTE", "Tinte 6.0", "PEL", "TIN", "Tinta"),
                ["APARATO"] = Producto("APARATO", "Radiofrecuencia", "APA", "001", "Fama"),
                ["MUESTRA"] = Producto("MUESTRA", "Muestra crema", "COS", "MMP", "Fama")
            };
            compras = new List<CompraDiaReposicion>();
            pendientes = new List<LineaPendienteReposicion>();
        }

        private static ProductoReposicion Producto(string id, string nombre, string grupo, string subgrupo, string familia,
            short estado = 0, bool ficticio = false)
            => new ProductoReposicion
            {
                Producto = id,
                Nombre = nombre,
                Grupo = grupo,
                SubGrupo = subgrupo,
                Familia = familia,
                Estado = estado,
                Ficticio = ficticio
            };

        /// <summary>Compras del cliente de un producto, cada una hace N días.</summary>
        private void Compras(string cliente, string producto, decimal importe, params int[] haceDias)
        {
            foreach (int dias in haceDias)
            {
                compras.Add(new CompraDiaReposicion
                {
                    Cliente = cliente + "  ",
                    Producto = producto + "     ",
                    Dia = HOY.AddDays(-dias),
                    BaseImponible = importe
                });
            }
        }

        private List<CandidatoReposicionDTO> Evaluar()
            => CalculadoraReposicion.EvaluarPares(compras, pendientes, productos, CONSUMIBLES, HOY);

        #region Intervalo

        [TestMethod]
        public void IntervaloMediano_ConMenosDeTresDiasDeCompra_EsNull()
        {
            Assert.IsNull(CalculadoraReposicion.IntervaloMediano(new[] { HOY, HOY.AddDays(-30) }));
        }

        [TestMethod]
        public void IntervaloMediano_VariasComprasElMismoDia_CuentanComoUna()
        {
            Assert.IsNull(CalculadoraReposicion.IntervaloMediano(new[] { HOY, HOY, HOY.AddDays(-30) }));
        }

        [TestMethod]
        public void IntervaloMediano_ImparDeIntervalos_EsElCentral()
        {
            // Intervalos 30, 30 y 100: la mediana (30) aguanta el pedido suelto de acopio mejor que la media
            DateTime[] dias = { HOY.AddDays(-160), HOY.AddDays(-130), HOY.AddDays(-100), HOY };
            Assert.AreEqual(30, CalculadoraReposicion.IntervaloMediano(dias));
        }

        [TestMethod]
        public void IntervaloMediano_ParDeIntervalos_EsLaMediaDeLosDosCentrales()
        {
            DateTime[] dias = { HOY.AddDays(-100), HOY.AddDays(-80), HOY.AddDays(-40) };
            Assert.AreEqual(30, CalculadoraReposicion.IntervaloMediano(dias)); // 20 y 40
        }

        [TestMethod]
        public void TocaReponer_EntreUnoCoVeinticincoYDosVecesElIntervalo()
        {
            // NestoAPI#736 (Carlos 29/10/26): el factor de abandono pasa de 3 a 2
            Assert.AreEqual(2d, CalculadoraReposicion.FACTOR_ABANDONO);
            Assert.IsFalse(CalculadoraReposicion.TocaReponer(40, 49));
            Assert.IsTrue(CalculadoraReposicion.TocaReponer(40, 50));
            Assert.IsTrue(CalculadoraReposicion.TocaReponer(40, 79));
            Assert.IsFalse(CalculadoraReposicion.TocaReponer(40, 80), "Con 2 veces el ritmo ya es un cliente que se ha ido");
            Assert.IsFalse(CalculadoraReposicion.TocaReponer(40, 119), "Con factor 3 sí habría tocado");
        }

        [TestMethod]
        public void EvaluarPares_RitmoDe150DiasYLleva320_YaNoToca()
        {
            // NestoAPI#736: el caso del primer envío (guantes de hace 10 meses con ritmo de 150-190 días). Con 3× tocaba.
            Compras("1001", "CREMA500", 50m, 620, 470, 320);
            Assert.AreEqual(0, Evaluar().Count);
        }

        [TestMethod]
        public void AplicarTope_MasDeLosDiasDelTope_SeQuedaFueraConMotivo()
        {
            Compras("1001", "CREMA500", 50m, 650, 470, 290);   // ritmo 180, lleva 290: toca (de 225 a 359)
            List<CandidatoReposicionDTO> candidatos = Evaluar();
            Assert.IsTrue(candidatos.Single().SeAvisaria, "Por ritmo toca");

            CalculadoraReposicion.AplicarTope(candidatos, 240);

            Assert.AreEqual("Última compra hace 290 días: más de 240.", candidatos.Single().Motivo);
            Assert.AreEqual("Última compra hace más de 240 días",
                RecordatorioReposicionJobsService.MotivoResumido(candidatos.Single().Motivo));
        }

        [TestMethod]
        public void AplicarTope_JustoEnElTope_SeAvisa()
        {
            var candidatos = new List<CandidatoReposicionDTO>
            {
                new CandidatoReposicionDTO { Cliente = "1", Producto = "A", DiasDesdeUltimaCompra = 240 },
                new CandidatoReposicionDTO { Cliente = "1", Producto = "B", DiasDesdeUltimaCompra = 241 },
                new CandidatoReposicionDTO { Cliente = "1", Producto = "C", DiasDesdeUltimaCompra = 300, Motivo = CalculadoraReposicion.MOTIVO_SIN_STOCK }
            };

            CalculadoraReposicion.AplicarTope(candidatos, 240);

            Assert.IsTrue(candidatos[0].SeAvisaria);
            Assert.AreEqual(CalculadoraReposicion.MotivoTope(241, 240), candidatos[1].Motivo);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_SIN_STOCK, candidatos[2].Motivo, "El motivo que ya tenía no se pisa");
        }

        [TestMethod]
        public void AplicarTope_CeroONull_SinTope()
        {
            var candidatos = new List<CandidatoReposicionDTO> { new CandidatoReposicionDTO { Cliente = "1", Producto = "A", DiasDesdeUltimaCompra = 700 } };

            CalculadoraReposicion.AplicarTope(candidatos, 0);
            CalculadoraReposicion.AplicarTope(candidatos, null);

            Assert.IsTrue(candidatos[0].SeAvisaria);
        }

        [TestMethod]
        public void TopeDias_Parametro_SinFila240_CeroOVacioSinTope()
        {
            Assert.AreEqual(240, ParametrosRecordatorioReposicion.InterpretarTopeDias(null));
            Assert.AreEqual(240, ParametrosRecordatorioReposicion.InterpretarTopeDias("180", error: true), "Si falla la lectura, el de por defecto");
            Assert.AreEqual(240, ParametrosRecordatorioReposicion.InterpretarTopeDias("mucho"));
            Assert.AreEqual(180, ParametrosRecordatorioReposicion.InterpretarTopeDias(" 180 "));
            Assert.IsNull(ParametrosRecordatorioReposicion.InterpretarTopeDias("0"));
            Assert.IsNull(ParametrosRecordatorioReposicion.InterpretarTopeDias(""));
            Assert.IsNull(ParametrosRecordatorioReposicion.InterpretarTopeDias("-5"));
        }

        [TestMethod]
        public void TopeDias_LectorQueFalla_240()
        {
            var lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new Exception("sin BD"));

            Assert.AreEqual(240, ParametrosRecordatorioReposicion.LeerTopeDias(lector));
            Assert.AreEqual(ModoPuertaModeloReposicion.Sombra, ParametrosRecordatorioReposicion.LeerModoPuerta(lector));
            Assert.AreEqual(0.15m, ParametrosRecordatorioReposicion.LeerUmbralModelo(lector));
        }

        [TestMethod]
        public void PuertaModelo_Parametros_SinFilaSombraY015()
        {
            Assert.AreEqual(ModoPuertaModeloReposicion.Sombra, ParametrosRecordatorioReposicion.InterpretarModoPuerta(null));
            Assert.AreEqual(ModoPuertaModeloReposicion.Sombra, ParametrosRecordatorioReposicion.InterpretarModoPuerta("lo que sea"));
            Assert.AreEqual(ModoPuertaModeloReposicion.Activo, ParametrosRecordatorioReposicion.InterpretarModoPuerta(" activo "));
            Assert.AreEqual(ModoPuertaModeloReposicion.Apagado, ParametrosRecordatorioReposicion.InterpretarModoPuerta("Apagado"));
            Assert.AreEqual(ModoPuertaModeloReposicion.Apagado, ParametrosRecordatorioReposicion.InterpretarModoPuerta("0"));
            Assert.AreEqual(0.15m, ParametrosRecordatorioReposicion.InterpretarUmbralModelo(null));
            Assert.AreEqual(0.2m, ParametrosRecordatorioReposicion.InterpretarUmbralModelo("0,2"));
            Assert.AreEqual(0.25m, ParametrosRecordatorioReposicion.InterpretarUmbralModelo("0.25"));
            Assert.AreEqual(0.15m, ParametrosRecordatorioReposicion.InterpretarUmbralModelo("7"), "Fuera de 0-1: el de por defecto");
        }

        #endregion

        #region Qué pares tocan

        [TestMethod]
        public void EvaluarPares_RitmoDe30DiasYLleva40SinComprar_SeAvisaria()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);

            CandidatoReposicionDTO candidato = Evaluar().Single();

            Assert.IsTrue(candidato.SeAvisaria);
            Assert.AreEqual("1001", candidato.Cliente);
            Assert.AreEqual("CREMA500", candidato.Producto);
            Assert.AreEqual(30, candidato.IntervaloDias);
            Assert.AreEqual(40, candidato.DiasDesdeUltimaCompra);
            Assert.AreEqual(3, candidato.NumeroCompras);
            Assert.AreEqual(50m, candidato.ImporteHabitual);
            Assert.AreEqual(HOY.AddDays(-40), candidato.UltimaCompra);
        }

        [TestMethod]
        public void EvaluarPares_TodaviaNoLeToca_NoAparece()
        {
            Compras("1001", "CREMA500", 50m, 90, 60, 30);
            Assert.AreEqual(0, Evaluar().Count);
        }

        [TestMethod]
        public void EvaluarPares_ClienteQueSeHaIdo_NoAparece()
        {
            Compras("1001", "CREMA500", 50m, 120, 90, 60);
            Assert.AreEqual(0, Evaluar().Count, "60 días con ritmo de 30 = 2 veces (NestoAPI#736): eso es otro correo (recuperar clientes)");
        }

        [TestMethod]
        public void EvaluarPares_SoloDosCompras_NoHayRitmoPropio()
        {
            Compras("1001", "CREMA500", 50m, 70, 40);
            Assert.AreEqual(0, Evaluar().Count);
        }

        [TestMethod]
        public void EvaluarPares_ComprasDeHaceMasDe24Meses_NoCuentan()
        {
            Compras("1001", "CREMA500", 50m, 800, 770, 40);
            Assert.AreEqual(0, Evaluar().Count);
        }

        [TestMethod]
        public void EvaluarPares_RitmoSemanal_NoSeAvisaNunca()
        {
            // Carlos (25/09/26): quien compra cada semana no necesita recordatorio, ni a los 10 días ni a los 18
            Compras("1001", "CREMA500", 50m, 31, 24, 17, 10);
            Assert.AreEqual(0, Evaluar().Count);

            compras.Clear();
            Compras("1001", "CREMA500", 50m, 39, 32, 25, 18);
            Assert.AreEqual(0, Evaluar().Count);

            // Cada 14 días ya no es semanal: se avisa a partir de 1,25 veces el ritmo
            compras.Clear();
            Compras("1001", "CREMA500", 50m, 60, 46, 32, 18);
            Assert.AreEqual(14, Evaluar().Single().IntervaloDias);
        }

        [TestMethod]
        public void NombreDePila_ElComercialPorSuNombre()
        {
            Assert.AreEqual("Lidia", CalculadoraReposicion.NombreDePila("LIDIA HERNÁNDEZ YAGÜE"));
            Assert.AreEqual("Mª José", CalculadoraReposicion.NombreDePila("Mª JOSÉ PÉREZ"));
            Assert.AreEqual("Ángela", CalculadoraReposicion.NombreDePila(" ángela  ruiz "));
            Assert.IsNull(CalculadoraReposicion.NombreDePila("  "));
        }

        [TestMethod]
        public void EvaluarPares_ImporteHabitual_EsLaMediaPorDiaDeCompra()
        {
            Compras("1001", "CREMA500", 30m, 100, 70);
            Compras("1001", "CREMA500", 90m, 40);
            Compras("1001", "CREMA500", 30m, 40); // Dos albaranes el mismo día: un solo día de compra
            Assert.AreEqual(60m, Evaluar().Single().ImporteHabitual);
        }

        [TestMethod]
        public void EvaluarPares_NoConsumibles_NoAparecen()
        {
            Compras("1001", "APARATO", 900m, 100, 70, 40);
            Compras("1001", "MUESTRA", 0m, 100, 70, 40);
            Compras("1001", "CUENTA7000", 10m, 100, 70, 40); // Línea de cuenta: no es un producto
            Assert.AreEqual(0, Evaluar().Count);
        }

        #endregion

        #region Cuándo no avisar

        [TestMethod]
        public void EvaluarPares_ProductoDeBaja_SeQuedaFueraConMotivo()
        {
            productos["CREMA500"].Estado = -1;
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_PRODUCTO_BAJA, Evaluar().Single().Motivo);
        }

        [TestMethod]
        public void EvaluarPares_ProductoFicticio_SeQuedaFuera()
        {
            productos["CREMA500"].Ficticio = true;
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_PRODUCTO_BAJA, Evaluar().Single().Motivo);
        }

        [TestMethod]
        public void EvaluarPares_ConPedidoPendienteDelProducto_SeQuedaFuera()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            pendientes.Add(new LineaPendienteReposicion { Cliente = "1001  ", Producto = "CREMA500  ", Estado = -1, Fecha = HOY.AddDays(3) });
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_PEDIDO_PENDIENTE, Evaluar().Single().Motivo);
        }

        [TestMethod]
        public void EvaluarPares_PedidoPendienteDeOtroCliente_NoLoQuita()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            pendientes.Add(new LineaPendienteReposicion { Cliente = "2002", Producto = "CREMA500", Estado = 1, Fecha = HOY });
            Assert.IsTrue(Evaluar().Single().SeAvisaria);
        }

        [TestMethod]
        public void EvaluarPares_NotaDeEntregaPosteriorALaUltimaCompra_SeQuedaFuera()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            pendientes.Add(new LineaPendienteReposicion { Cliente = "1001", Producto = "CREMA500", Estado = -2, Fecha = HOY.AddDays(-10) });
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_NOTA_ENTREGA, Evaluar().Single().Motivo);
        }

        [TestMethod]
        public void EvaluarPares_NotaDeEntregaAnterior_NoLoQuita()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            pendientes.Add(new LineaPendienteReposicion { Cliente = "1001", Producto = "CREMA500", Estado = -2, Fecha = HOY.AddDays(-200) });
            Assert.IsTrue(Evaluar().Single().SeAvisaria);
        }

        [TestMethod]
        public void EvaluarPares_DespuesComproOtroFormatoDeLaMismaMarca_SeQuedaFuera()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            Compras("1001", "CREMA1000", 80m, 20);
            CandidatoReposicionDTO candidato = Evaluar().Single(c => c.Producto == "CREMA500");
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_SUSTITUTO + "CREMA1000", candidato.Motivo);
        }

        [TestMethod]
        public void EvaluarPares_ElSustitutoSeComproAntes_NoLoQuita()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            Compras("1001", "CREMA1000", 80m, 60);
            Assert.IsTrue(Evaluar().Single(c => c.Producto == "CREMA500").SeAvisaria);
        }

        [TestMethod]
        public void EvaluarPares_DespuesComproOtraMarca_NoEsSustituto()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            Compras("1001", "CREMAOTRA", 80m, 20);
            Assert.IsTrue(Evaluar().Single(c => c.Producto == "CREMA500").SeAvisaria);
        }

        [TestMethod]
        public void EvaluarPares_SustitutoEnUnPedidoPendiente_SeQuedaFuera()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            pendientes.Add(new LineaPendienteReposicion { Cliente = "1001", Producto = "CREMA1000", Estado = 1, Fecha = HOY });
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_SUSTITUTO_PENDIENTE + "CREMA1000", Evaluar().Single().Motivo);
        }

        [TestMethod]
        public void AplicarSinStock_MarcaSoloLosQueSeAvisarian()
        {
            var candidatos = new List<CandidatoReposicionDTO>
            {
                new CandidatoReposicionDTO { Cliente = "1", Producto = "CREMA500" },
                new CandidatoReposicionDTO { Cliente = "2", Producto = "CREMA500", Motivo = CalculadoraReposicion.MOTIVO_PEDIDO_PENDIENTE },
                new CandidatoReposicionDTO { Cliente = "3", Producto = "TINTE" }
            };

            CalculadoraReposicion.AplicarSinStock(candidatos, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crema500" });

            Assert.AreEqual(CalculadoraReposicion.MOTIVO_SIN_STOCK, candidatos[0].Motivo);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_PEDIDO_PENDIENTE, candidatos[1].Motivo);
            Assert.IsTrue(candidatos[2].SeAvisaria);
        }

        [TestMethod]
        public void ProductosSinStock_DisponibleEnSedesMenosPendienteMasTraspasos()
        {
            var resumen = new ResumenStocksProductos();
            resumen.StockSedes["CREMA500"] = 5;
            resumen.PendienteEntregarTotal["CREMA500"] = 5; // Todo comprometido
            resumen.StockSedes["TINTE"] = 2;
            resumen.PendienteEntregarTotal["TINTE"] = 3;
            resumen.PendienteReposicion["TINTE"] = 4; // Viene por traspaso
            resumen.StockSedes["CREMA1000"] = 1;

            HashSet<string> sinStock = CalculadoraReposicion.ProductosSinStock(resumen,
                new[] { "CREMA500", "TINTE", "CREMA1000", "NADA" });

            CollectionAssert.AreEquivalent(new[] { "CREMA500", "NADA" }, sinStock.ToList());
        }

        #endregion

        #region Agrupar por cliente

        private static CandidatoReposicionDTO Candidato(string cliente, string producto, decimal importe, string motivo = null)
            => new CandidatoReposicionDTO { Cliente = cliente, Producto = producto, ImporteHabitual = importe, Motivo = motivo };

        private static ClienteReposicion Cliente(string id, string email, short estado = 0, string vendedor = "LHY", string vendedorNombre = "Lidia")
            => new ClienteReposicion { Cliente = id, Nombre = "CLIENTE " + id, Email = email, Estado = estado, Vendedor = vendedor, VendedorNombre = vendedorNombre };

        private static string ClienteFueraDelControl(int desde = 1000)
        {
            for (int i = desde; ; i++)
            {
                if (!CalculadoraReposicion.EsGrupoControl(i.ToString()))
                {
                    return i.ToString();
                }
            }
        }

        private static string ClienteDelControl()
        {
            for (int i = 1000; ; i++)
            {
                if (CalculadoraReposicion.EsGrupoControl(i.ToString()))
                {
                    return i.ToString();
                }
            }
        }

        [TestMethod]
        public void Agrupar_UnCorreoPorClienteConComoMuchoTresProductosLosDeMasImporte()
        {
            string c = ClienteFueraDelControl();
            var candidatos = new List<CandidatoReposicionDTO>
            {
                Candidato(c, "A", 10m), Candidato(c, "B", 40m), Candidato(c, "C", 30m), Candidato(c, "D", 20m)
            };
            var clientes = new Dictionary<string, ClienteReposicion>(StringComparer.OrdinalIgnoreCase) { [c] = Cliente(c, "ana@x.es") };

            ResultadoRecordatorioReposicionDTO resultado = CalculadoraReposicion.Agrupar(candidatos, clientes, HOY);

            RecordatorioReposicionClienteDTO correo = resultado.SeAvisarian.Single();
            CollectionAssert.AreEqual(new[] { "B", "C", "D" }, correo.Productos.Select(p => p.Producto).ToList());
            Assert.AreEqual("A", resultado.Descartes.Single().Producto);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_MAXIMO_PRODUCTOS, resultado.Descartes.Single().Motivo);
            Assert.AreEqual("Lidia", correo.VendedorNombre);
        }

        [TestMethod]
        public void Agrupar_ClienteSinCorreoODeBaja_SeQuedaFuera()
        {
            string sinCorreo = ClienteFueraDelControl(1000);
            string deBaja = ClienteFueraDelControl(int.Parse(sinCorreo) + 1);
            var candidatos = new List<CandidatoReposicionDTO> { Candidato(sinCorreo, "A", 10m), Candidato(deBaja, "A", 10m), Candidato("9999999", "A", 1m) };
            var clientes = new Dictionary<string, ClienteReposicion>(StringComparer.OrdinalIgnoreCase)
            {
                [sinCorreo] = Cliente(sinCorreo, " "),
                [deBaja] = Cliente(deBaja, "baja@x.es", estado: 8)
            };

            ResultadoRecordatorioReposicionDTO resultado = CalculadoraReposicion.Agrupar(candidatos, clientes, HOY);

            Assert.AreEqual(0, resultado.SeAvisarian.Count);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_SIN_CORREO, resultado.Descartes.Single(d => d.Cliente == sinCorreo).Motivo);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_CLIENTE_BAJA, resultado.Descartes.Single(d => d.Cliente == deBaja).Motivo);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_CLIENTE_NO_ENCONTRADO, resultado.Descartes.Single(d => d.Cliente == "9999999").Motivo);
        }

        [TestMethod]
        public void Agrupar_DosClientesConElMismoCorreo_SoloUnCorreo()
        {
            string uno = ClienteFueraDelControl(1000);
            string otro = ClienteFueraDelControl(int.Parse(uno) + 1);
            var candidatos = new List<CandidatoReposicionDTO>
            {
                Candidato(uno, "A", 10m), Candidato(otro, "A", 10m), Candidato(otro, "B", 10m)
            };
            var clientes = new Dictionary<string, ClienteReposicion>(StringComparer.OrdinalIgnoreCase)
            {
                [uno] = Cliente(uno, "Salon@x.es"),
                [otro] = Cliente(otro, "salon@x.es ")
            };

            ResultadoRecordatorioReposicionDTO resultado = CalculadoraReposicion.Agrupar(candidatos, clientes, HOY);

            Assert.AreEqual(otro, resultado.SeAvisarian.Single().Cliente);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_CORREO_DUPLICADO + otro, resultado.Descartes.Single().Motivo);
        }

        [TestMethod]
        public void Agrupar_ClienteDelGrupoDeControl_NoSeLeEscribe()
        {
            string control = ClienteDelControl();
            var candidatos = new List<CandidatoReposicionDTO> { Candidato(control, "A", 10m) };
            var clientes = new Dictionary<string, ClienteReposicion>(StringComparer.OrdinalIgnoreCase) { [control] = Cliente(control, "c@x.es") };

            ResultadoRecordatorioReposicionDTO resultado = CalculadoraReposicion.Agrupar(candidatos, clientes, HOY);

            Assert.AreEqual(0, resultado.SeAvisarian.Count);
            Assert.AreEqual(control, resultado.GrupoControl.Single().Cliente);
        }

        [TestMethod]
        public void EsGrupoControl_EsEstableYRondaElDiezPorCiento()
        {
            int enControl = Enumerable.Range(10000, 5000).Count(i => CalculadoraReposicion.EsGrupoControl(i.ToString()));
            Assert.IsTrue(enControl > 350 && enControl < 650, $"{enControl} de 5000");
            Assert.AreEqual(CalculadoraReposicion.EsGrupoControl("15191"), CalculadoraReposicion.EsGrupoControl(" 15191 "));
            Assert.IsFalse(CalculadoraReposicion.EsGrupoControl("15191", 0));
        }

        [TestMethod]
        public void NombreVendedor_ElVendedorGeneralNoEsUnaPersona()
        {
            Assert.IsNull(CalculadoraReposicion.NombreVendedor(Cliente("1", "a@x.es", vendedor: "NV ", vendedorNombre: "General")));
            Assert.AreEqual("Lidia", CalculadoraReposicion.NombreVendedor(Cliente("1", "a@x.es")));
        }

        #endregion

        #region Consumibles

        [TestMethod]
        public void Consumibles_PorDefecto()
        {
            Assert.IsTrue(CONSUMIBLES.EsConsumible("COS", "125"));
            Assert.IsTrue(CONSUMIBLES.EsConsumible("COS ", "201"));
            Assert.IsFalse(CONSUMIBLES.EsConsumible("COS", "MMP"), "Muestras y material promocional");
            Assert.IsTrue(CONSUMIBLES.EsConsumible("PEL", "TIN"));
            Assert.IsFalse(CONSUMIBLES.EsConsumible("PEL", "PEI"));
            Assert.IsTrue(CONSUMIBLES.EsConsumible("ACC", "002"));
            // Carlos (25/09/26): manicura, pedicura y utillaje también se compran cíclicamente
            Assert.IsTrue(CONSUMIBLES.EsConsumible("ACC", "005"), "Manicura");
            Assert.IsTrue(CONSUMIBLES.EsConsumible("ACC", "006"), "Pedicura");
            Assert.IsTrue(CONSUMIBLES.EsConsumible("PEL", "UTJ"), "Utillaje");
            Assert.IsTrue(CONSUMIBLES.EsConsumible("PEL", "PEL"), "Utillaje.");
            Assert.IsFalse(CONSUMIBLES.EsConsumible("ACC", "001"), "Vestuario");
            Assert.IsFalse(CONSUMIBLES.EsConsumible("APA", "001"));
            Assert.IsFalse(CONSUMIBLES.EsConsumible("CUR", "INI"));
        }

        [TestMethod]
        public void Consumibles_DesdeParametro()
        {
            ConsumiblesReposicion lista = ConsumiblesReposicion.Leer("PEL; ACC/002 ; -PEL/APA");
            Assert.IsTrue(lista.EsConsumible("PEL", "TIN"));
            Assert.IsFalse(lista.EsConsumible("PEL", "APA"));
            Assert.IsTrue(lista.EsConsumible("ACC", "002"));
            Assert.IsFalse(lista.EsConsumible("COS", "125"));
        }

        [TestMethod]
        public void Consumibles_ParametroVacioOInvalido_UsaLaListaPorDefecto()
        {
            Assert.AreEqual(ConsumiblesReposicion.POR_DEFECTO, ConsumiblesReposicion.Leer("").Definicion);
            Assert.AreEqual(ConsumiblesReposicion.POR_DEFECTO, ConsumiblesReposicion.Leer(" -COS/MMP ").Definicion);
        }

        #endregion

        #region NestoAPI#661: habituales de un cliente

        private void ComprasConCantidad(string cliente, string producto, decimal importe, int cantidad, params int[] haceDias)
        {
            foreach (int dias in haceDias)
            {
                compras.Add(new CompraDiaReposicion
                {
                    Cliente = cliente,
                    Producto = producto,
                    Dia = HOY.AddDays(-dias),
                    BaseImponible = importe,
                    Cantidad = cantidad
                });
            }
        }

        private List<HabitualClienteDTO> Habituales(Func<IReadOnlyCollection<string>, ISet<string>> leerSinStock = null)
            => CalculadoraReposicion.Habituales(compras, pendientes, productos, CONSUMIBLES, HOY, leerSinStock);

        [TestMethod]
        public void Habituales_TresComprasCada30DiasY40DesdeLaUltima_TocaReponer()
        {
            ComprasConCantidad("1001", "CREMA500", 50m, 2, 100, 70, 40);

            HabitualClienteDTO habitual = Habituales().Single();

            Assert.IsTrue(habitual.TocaReponer);
            Assert.AreEqual("CREMA500", habitual.Producto);
            Assert.AreEqual("Crema hidratante 500 ml", habitual.Nombre);
            Assert.AreEqual(2, habitual.CantidadHabitual);
            Assert.AreEqual(HOY.AddDays(-40), habitual.UltimaCompra);
            Assert.AreEqual(1, habitual.Orden);
        }

        [TestMethod]
        public void Habituales_TresComprasCada30DiasY20DesdeLaUltima_AparecePeroNoToca()
        {
            ComprasConCantidad("1001", "CREMA500", 50m, 1, 80, 50, 20);

            HabitualClienteDTO habitual = Habituales().Single();

            Assert.IsFalse(habitual.TocaReponer);
        }

        [TestMethod]
        public void Habituales_MedianaDeMenosDe14Dias_NuncaToca()
        {
            // Ritmo de 10 días y 21 desde la última: por ventana tocaría (12,5 a 30), pero quien compra tan a
            // menudo no necesita recordatorio
            ComprasConCantidad("1001", "CREMA500", 50m, 1, 41, 31, 21);

            Assert.IsFalse(Habituales().Single().TocaReponer);
        }

        [TestMethod]
        public void Habituales_ConPedidoPendienteDelMismoCliente_NoToca()
        {
            ComprasConCantidad("1001", "CREMA500", 50m, 1, 100, 70, 40);
            pendientes.Add(new LineaPendienteReposicion { Cliente = "1001", Producto = "CREMA500", Estado = -1, Fecha = HOY.AddDays(2) });

            HabitualClienteDTO habitual = Habituales().Single();

            Assert.IsFalse(habitual.TocaReponer, "Ya lo tiene pedido");
        }

        [TestMethod]
        public void Habituales_SinStock_NoToca()
        {
            ComprasConCantidad("1001", "CREMA500", 50m, 1, 100, 70, 40);

            HabitualClienteDTO habitual = Habituales(_ => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CREMA500" }).Single();

            Assert.IsFalse(habitual.TocaReponer);
        }

        [TestMethod]
        public void Habituales_SinCompras_ListaVacia()
        {
            Assert.AreEqual(0, Habituales().Count);
        }

        [TestMethod]
        public void Habituales_NoConsumiblesYCompraSuelta_AparecenSinTocar_YLosDeBajaNoAparecen()
        {
            ComprasConCantidad("1001", "APARATO", 900m, 1, 100, 70, 40);   // no consumible, con el mismo ritmo
            ComprasConCantidad("1001", "TINTE", 5m, 1, 10);                 // una sola compra: sin ritmo
            productos["BAJA"] = Producto("BAJA", "Crema descatalogada", "COS", "125", "Fama", estado: -1);
            productos["FICTICIO"] = Producto("FICTICIO", "Portes", "COS", "125", "Fama", ficticio: true);
            ComprasConCantidad("1001", "BAJA", 50m, 1, 100, 70, 40);
            ComprasConCantidad("1001", "FICTICIO", 50m, 1, 100, 70, 40);
            ComprasConCantidad("1001", "SINFICHA", 50m, 1, 100, 70, 40);

            List<HabitualClienteDTO> habituales = Habituales();

            CollectionAssert.AreEquivalent(new[] { "APARATO", "TINTE" }, habituales.Select(h => h.Producto).ToArray());
            Assert.IsTrue(habituales.All(h => !h.TocaReponer));
        }

        [TestMethod]
        public void Habituales_Orden_PrimeroLosQueTocanPorImporteYLuegoElRestoPorImporteYUltimaCompra()
        {
            ComprasConCantidad("1001", "CREMA500", 20m, 1, 100, 70, 40);   // toca, importe 20
            ComprasConCantidad("1001", "CREMA1000", 60m, 1, 100, 70, 40);  // toca, importe 60
            ComprasConCantidad("1001", "APARATO", 900m, 1, 100, 70, 40);   // no toca (no consumible), importe 900
            ComprasConCantidad("1001", "TINTE", 5m, 1, 30);                // no toca, importe 5, más reciente
            ComprasConCantidad("1001", "MUESTRA", 5m, 1, 60);              // no toca, importe 5, más antigua

            List<HabitualClienteDTO> habituales = Habituales();

            CollectionAssert.AreEqual(new[] { "CREMA1000", "CREMA500", "APARATO", "TINTE", "MUESTRA" },
                habituales.Select(h => h.Producto).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, habituales.Select(h => h.Orden).ToArray());
        }

        [TestMethod]
        public void CantidadHabitual_EsLaMedianaRedondeadaYNuncaMenosDeUno()
        {
            Assert.AreEqual(2, CalculadoraReposicion.CantidadHabitual(new[] { 2, 6, 2 }), "Un pedido grande suelto no la mueve");
            Assert.AreEqual(2, CalculadoraReposicion.CantidadHabitual(new[] { 1, 2 }), "1,5 redondea hacia arriba (AwayFromZero)");
            Assert.AreEqual(4, CalculadoraReposicion.CantidadHabitual(new[] { 2, 3, 4, 5 }), "(3 + 4) / 2 = 3,5 → 4");
            Assert.AreEqual(1, CalculadoraReposicion.CantidadHabitual(new int[0]));
        }

        [TestMethod]
        public void EvaluarPares_LlevaLaCantidadHabitual()
        {
            ComprasConCantidad("1001", "CREMA500", 50m, 3, 100);
            ComprasConCantidad("1001", "CREMA500", 50m, 4, 70);
            ComprasConCantidad("1001", "CREMA500", 50m, 3, 40);

            Assert.AreEqual(3, Evaluar().Single().CantidadHabitual);
        }

        #endregion
    }
}
