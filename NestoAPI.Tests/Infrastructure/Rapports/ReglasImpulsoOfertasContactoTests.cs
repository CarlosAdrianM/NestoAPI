using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Rapports;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>NestoAPI#866 (corte 1): el impulso por las ofertas vigentes del día, regla pura.</summary>
    [TestClass]
    public class ReglasImpulsoOfertasContactoTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 16);

        private static ClienteCarteraContacto Cliente(string id, decimal importe12 = 1000, int? diasContacto = 45, int pedidos12 = 8) => new ClienteCarteraContacto
        {
            Cliente = id,
            Contacto = "0",
            Nombre = "Centro " + id,
            Pedidos12Meses = pedidos12,
            Pedidos24Meses = pedidos12,
            Importe12Meses = importe12,
            UltimoPedido = HOY.AddDays(-30),
            UltimoContacto = diasContacto.HasValue ? HOY.AddDays(-diasContacto.Value) : (DateTime?)null
        };

        private static CompraProductoCliente Compra(string cliente, string producto, int haceDias, decimal importe = 100)
            => new CompraProductoCliente { Cliente = cliente, Contacto = "0", Producto = producto, Dia = HOY.AddDays(-haceDias), Importe = importe };

        private static OfertaProductoDia Oferta(string producto, decimal descuento, DateTime? desde = null, DateTime? hasta = null, string nombre = "CREMA HIDRATANTE")
            => new OfertaProductoDia
            {
                Producto = producto,
                Nombre = nombre,
                Descuento = descuento,
                Texto = $"un {ReglasImpulsoOfertasContacto.Porcentaje(descuento)} % de descuento",
                FechaDesde = desde,
                FechaHasta = hasta
            };

        private static Dictionary<string, OfertaProductoDia> Ofertas(params OfertaProductoDia[] ofertas)
            => ofertas.ToDictionary(o => o.Producto, o => o, StringComparer.OrdinalIgnoreCase);

        // ---------------- Interruptor ----------------

        [TestMethod]
        public void Interpretar_SinFilaOUnoEncendido_CeroYSinonimosApagado()
        {
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Interpretar(null));
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Interpretar("1"));
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Interpretar("sí"));
            foreach (string apagado in new[] { "0", "false", "No", " off ", "APAGADO" })
            {
                Assert.IsFalse(ReglasImpulsoOfertasContacto.Interpretar(apagado), apagado);
            }
        }

        // ---------------- Ofertas por producto ----------------

        private static readonly ProductoOfertable Crema = new ProductoOfertable
        {
            Producto = "45929", Nombre = "CREMA HIDRATANTE 50 ML", Familia = "Eva", Grupo = "COS", SubGrupo = "CRE", PVP = 10m
        };

        [TestMethod]
        public void Casa_ProductoExactoOFamiliaGrupoSubgrupoYPrefijoEnAnd()
        {
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Casa(new FilaOfertaDia { Producto = "45929 " }, Crema));
            Assert.IsFalse(ReglasImpulsoOfertasContacto.Casa(new FilaOfertaDia { Producto = "45930" }, Crema));
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Casa(new FilaOfertaDia { Familia = "EVA" }, Crema));
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Casa(new FilaOfertaDia { Familia = "Eva", Grupo = "COS", SubGrupo = "CRE", FiltroProducto = "crema" }, Crema));
            Assert.IsFalse(ReglasImpulsoOfertasContacto.Casa(new FilaOfertaDia { Familia = "Eva", SubGrupo = "ACE" }, Crema));
            Assert.IsFalse(ReglasImpulsoOfertasContacto.Casa(new FilaOfertaDia { Familia = "Eva", FiltroProducto = "SERUM" }, Crema));
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Casa(new FilaOfertaDia { Grupo = "COS" }, Crema));
            Assert.IsFalse(ReglasImpulsoOfertasContacto.Casa(new FilaOfertaDia { SubGrupo = "CRE" }, Crema), "sin producto, familia ni grupo no es oferta de nadie");
        }

        [TestMethod]
        public void DescuentoDe_NmasMPrecioEspecialYDescuento()
        {
            Assert.AreEqual(1m / 7m, ReglasImpulsoOfertasContacto.DescuentoDe(new FilaOfertaDia { CantidadConPrecio = 6, CantidadRegalo = 1 }, Crema));
            Assert.AreEqual(0.2m, ReglasImpulsoOfertasContacto.DescuentoDe(new FilaOfertaDia { Precio = 8m, Descuento = 0.5m }, Crema), "el precio especial manda");
            Assert.AreEqual(0.5m, ReglasImpulsoOfertasContacto.DescuentoDe(new FilaOfertaDia { Precio = 8m, Descuento = 0.5m }, new ProductoOfertable { Producto = "1" }), "sin PVP, el descuento");
            Assert.AreEqual(0m, ReglasImpulsoOfertasContacto.DescuentoDe(new FilaOfertaDia { Precio = 12m }, Crema), "precio por encima del PVP: nada");
        }

        [TestMethod]
        public void OfertasPorProducto_LaDeMasDescuentoConSuTextoYFechas()
        {
            var filas = new List<FilaOfertaDia>
            {
                new FilaOfertaDia { Familia = "Eva", Descuento = 0.1m, FechaHasta = new DateTime(2026, 12, 31) },
                new FilaOfertaDia { Producto = "45929", CantidadConPrecio = 6, CantidadRegalo = 1, FechaDesde = new DateTime(2026, 11, 12), FechaHasta = new DateTime(2026, 11, 20) },
                new FilaOfertaDia { Producto = "45929", Descuento = 0m }
            };

            Dictionary<string, OfertaProductoDia> ofertas = ReglasImpulsoOfertasContacto.OfertasPorProducto(filas,
                new[] { Crema, new ProductoOfertable { Producto = "11111", Familia = "Otra" } });

            Assert.AreEqual(1, ofertas.Count);
            OfertaProductoDia oferta = ofertas["45929"];
            Assert.AreEqual("el 6+1", oferta.Texto);
            Assert.AreEqual(new DateTime(2026, 11, 20), oferta.FechaHasta);
            Assert.AreEqual("CREMA HIDRATANTE 50 ML", oferta.Nombre);
        }

        // ---------------- N+M sin fechas (18/11/26): red de 90 días ----------------

        [TestMethod]
        public void CuentaOfertaNm_SinFechas_CuentaDentroDe90DiasYA91No()
        {
            int dias = ReglasImpulsoOfertasContacto.DIAS_NM_SIN_FECHAS;
            Assert.IsTrue(ReglasImpulsoOfertasContacto.CuentaOfertaNm(null, null, HOY.AddDays(-dias).AddHours(17), HOY), "a 90 días cuenta");
            Assert.IsTrue(ReglasImpulsoOfertasContacto.CuentaOfertaNm(null, null, HOY.AddHours(9), HOY), "modificada hoy");
            Assert.IsFalse(ReglasImpulsoOfertasContacto.CuentaOfertaNm(null, null, HOY.AddDays(-dias - 1), HOY), "a 91 no");
        }

        [TestMethod]
        public void CuentaOfertaNm_ConFechas_ComoSiempre_SinMirarLaModificacion()
        {
            DateTime antigua = HOY.AddYears(-2);
            Assert.IsTrue(ReglasImpulsoOfertasContacto.CuentaOfertaNm(HOY.AddDays(-3), HOY.AddDays(10), antigua, HOY));
            Assert.IsTrue(ReglasImpulsoOfertasContacto.CuentaOfertaNm(null, HOY, antigua, HOY), "el último día cuenta");
            Assert.IsFalse(ReglasImpulsoOfertasContacto.CuentaOfertaNm(null, HOY.AddDays(-1), HOY, HOY), "caducada, aunque se modificara hoy");
            Assert.IsFalse(ReglasImpulsoOfertasContacto.CuentaOfertaNm(HOY.AddDays(1), null, HOY, HOY), "aún no ha empezado");
        }

        [TestMethod]
        public void NmSinFechas_LaFechaDeEntradaEsLaModificacion_SinFin()
        {
            var filas = new List<FilaOfertaDia>
            {
                new FilaOfertaDia { Producto = "45929", CantidadConPrecio = 6, CantidadRegalo = 1, FechaModificacion = HOY.AddDays(-3).AddHours(11) }
            };

            OfertaProductoDia oferta = ReglasImpulsoOfertasContacto.OfertasPorProducto(filas, new[] { Crema })["45929"];

            Assert.AreEqual(HOY.AddDays(-3), oferta.FechaEntrada);
            Assert.IsNull(oferta.FechaDesde);
            Assert.IsNull(oferta.FechaHasta);

            var compras = new[] { Compra("1001", "45929", 120), Compra("1001", "45929", 60) };
            ClienteConOfertas con = ReglasImpulsoOfertasContacto.Evaluar(new[] { Cliente("1001", diasContacto: 2) }, compras,
                new Dictionary<string, OfertaProductoDia>(StringComparer.OrdinalIgnoreCase) { ["45929"] = oferta }, HOY)["1001/0"];

            Assert.IsTrue(con.Mejor.RecienEntrada, "entró hace 3 días");
            Assert.IsFalse(con.Mejor.CaducaPronto, "no tiene fin");
            Assert.IsTrue(con.Excluido, "se habló con él hace 2 días, después de entrar la oferta");
            Assert.AreEqual($" (oferta nueva, desde el {HOY.AddDays(-3):dd/MM})", ReglasImpulsoOfertasContacto.TextoVigencia(con.Mejor, HOY));
            StringAssert.DoesNotMatch(ReglasImpulsoOfertasContacto.Motivo(con, HOY), new System.Text.RegularExpressions.Regex("hasta el"));
        }

        [TestMethod]
        public void NmSinFechas_HabladoAntesDeEntrar_NoExcluido()
        {
            var oferta = new OfertaProductoDia { Producto = "45929", Nombre = "CREMA", Descuento = 1m / 7m, Texto = "el 6+1", FechaEntrada = HOY.AddDays(-3) };
            var compras = new[] { Compra("1001", "45929", 120), Compra("1001", "45929", 60) };

            ClienteConOfertas con = ReglasImpulsoOfertasContacto.Evaluar(new[] { Cliente("1001", diasContacto: 5) }, compras,
                new Dictionary<string, OfertaProductoDia>(StringComparer.OrdinalIgnoreCase) { ["45929"] = oferta }, HOY)["1001/0"];

            Assert.IsFalse(con.Excluido, "se habló con él antes de que entrara la oferta");
            Assert.IsTrue(con.PuedeSubir);
        }

        // ---------------- Ciclo y puntuación ----------------

        [TestMethod]
        public void Ciclo_MedianaDeLosDiasEntreCompras()
        {
            DateTime d = new DateTime(2026, 1, 1);
            Assert.AreEqual(45, ReglasImpulsoOfertasContacto.Ciclo(new[] { d, d.AddDays(40), d.AddDays(90) }), "huecos 40 y 50");
            Assert.AreEqual(30, ReglasImpulsoOfertasContacto.Ciclo(new[] { d, d.AddDays(30), d.AddDays(60), d.AddDays(160) }), "huecos 30, 30, 100: la mediana no se deja arrastrar");
            Assert.AreEqual(10, ReglasImpulsoOfertasContacto.Ciclo(new[] { d.AddDays(10), d, d, d.AddDays(10).AddHours(5) }), "días distintos y desordenados");
            Assert.AreEqual(0, ReglasImpulsoOfertasContacto.Ciclo(new[] { d }));
        }

        [TestMethod]
        public void Puntuacion_PesaMasLoQueYaLeTocaLuegoDescuentoYPeso()
        {
            double toca = ReglasImpulsoOfertasContacto.Puntuacion(45, 64, 0.2m, 0.1m, false, false);
            double noToca = ReglasImpulsoOfertasContacto.Puntuacion(45, 20, 0.5m, 0.5m, false, false);
            Assert.IsTrue(toca > noToca, "un producto que le toca gana aunque el otro tenga más descuento y peso");
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Puntuacion(45, 64, 0.3m, 0.1m, false, false) > toca, "más descuento, más");
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Puntuacion(45, 64, 0.2m, 0.3m, false, false) > toca, "más peso, más");
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Puntuacion(45, 64, 0.2m, 0.1m, false, true) > toca, "si caduca pronto, más");
            Assert.AreEqual(ReglasImpulsoOfertasContacto.Puntuacion(45, 90, 0.2m, 0.1m, false, false),
                ReglasImpulsoOfertasContacto.Puntuacion(45, 300, 0.2m, 0.1m, false, false), "lo pasado de ciclo satura en el doble");
        }

        // ---------------- Evaluar ----------------

        [TestMethod]
        public void Evaluar_SoloProductosEnOfertaConDosComprasOMas()
        {
            var cartera = new[] { Cliente("1001") };
            var compras = new[]
            {
                Compra("1001", "45929", 154, 200), Compra("1001", "45929", 109, 200), Compra("1001", "45929", 64, 200),
                Compra("1001", "50000", 100), Compra("1001", "50000", 50),     // no está en oferta
                Compra("1001", "60000", 10)                                    // una sola compra
            };

            Dictionary<string, ClienteConOfertas> resultado = ReglasImpulsoOfertasContacto.Evaluar(cartera, compras,
                Ofertas(Oferta("45929", 0.2m), Oferta("60000", 0.5m)), HOY);

            ClienteConOfertas con = resultado["1001/0"];
            Assert.AreEqual(1, con.Productos.Count);
            ProductoEnOfertaCliente p = con.Mejor;
            Assert.AreEqual("45929", p.Producto);
            Assert.AreEqual(3, p.Compras);
            Assert.AreEqual(45, p.CicloDias);
            Assert.AreEqual(64, p.DiasDesdeUltima);
            Assert.IsTrue(p.LeToca);
            Assert.AreEqual(0.6m, p.Peso);
            Assert.IsFalse(con.Excluido);
            Assert.IsTrue(con.PuedeSubir);
        }

        [TestMethod]
        public void Evaluar_OfertaRecienEntradaYQueCaducaPronto()
        {
            var compras = new[] { Compra("1001", "1", 60), Compra("1001", "1", 30), Compra("1001", "2", 60), Compra("1001", "2", 30) };

            ClienteConOfertas con = ReglasImpulsoOfertasContacto.Evaluar(new[] { Cliente("1001") }, compras,
                Ofertas(Oferta("1", 0.1m, desde: HOY.AddDays(-7), hasta: HOY.AddDays(30)), Oferta("2", 0.1m, desde: HOY.AddDays(-8), hasta: HOY.AddDays(7))), HOY)["1001/0"];

            ProductoEnOfertaCliente uno = con.Productos.Single(p => p.Producto == "1");
            ProductoEnOfertaCliente dos = con.Productos.Single(p => p.Producto == "2");
            Assert.IsTrue(uno.RecienEntrada);
            Assert.IsFalse(uno.CaducaPronto);
            Assert.IsFalse(dos.RecienEntrada);
            Assert.IsTrue(dos.CaducaPronto);
            Assert.AreEqual("2", con.Mejor.Producto, "a igualdad, la que caduca pronto puntúa más que la recién entrada");
        }

        [TestMethod]
        public void Excluido_HabladoDesdeQueEntroLaOfertaEnLasDosUltimasSemanas()
        {
            var mejor = new ProductoEnOfertaCliente { FechaDesde = HOY.AddDays(-5) };

            Assert.IsTrue(ReglasImpulsoOfertasContacto.Excluido(Cliente("1", diasContacto: 3), mejor, HOY), "hablado tras entrar la oferta");
            Assert.IsFalse(ReglasImpulsoOfertasContacto.Excluido(Cliente("1", diasContacto: 6), mejor, HOY), "hablado antes de la oferta: no la conoce");
            Assert.IsFalse(ReglasImpulsoOfertasContacto.Excluido(Cliente("1", diasContacto: 20), new ProductoEnOfertaCliente { FechaDesde = HOY.AddDays(-50) }, HOY),
                "una oferta larga: hace más de 2 semanas ya no cuenta");
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Excluido(Cliente("1", diasContacto: 10), new ProductoEnOfertaCliente(), HOY), "sin fecha de entrada: 2 semanas");
            Assert.IsFalse(ReglasImpulsoOfertasContacto.Excluido(Cliente("1", diasContacto: null), mejor, HOY));

            ClienteCarteraContacto intento = Cliente("1", diasContacto: null);
            intento.UltimoIntento = HOY.AddDays(-1);
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Excluido(intento, mejor, HOY), "no contestó ayer");
            ClienteCarteraContacto pidio = Cliente("1", diasContacto: null);
            pidio.UltimoPedidoCliente = HOY.AddDays(-3);
            Assert.IsTrue(ReglasImpulsoOfertasContacto.Excluido(pidio, mejor, HOY), "pidió hace 3 días");
        }

        // ---------------- Aplicar ----------------

        private static ClienteConOfertas Con(string id, double puntuacion, bool leToca = true, bool excluido = false) => new ClienteConOfertas
        {
            Cliente = Cliente(id),
            Excluido = excluido,
            Productos = new List<ProductoEnOfertaCliente>
            {
                new ProductoEnOfertaCliente { Producto = "45929", Nombre = "CREMA", CicloDias = 45, DiasDesdeUltima = leToca ? 64 : 10, LeToca = leToca,
                    Descuento = 0.2m, TextoOferta = "un 20 % de descuento", Puntuacion = puntuacion }
            }
        };

        private static SugerenciaContactoDTO Sugerida(string id, string origen = OrigenesSugerenciaContacto.PROBABILIDAD)
            => new SugerenciaContactoDTO { Cliente = id, Contacto = "0", Origen = origen, Prioridad = PrioridadesContacto.ALTA };

        [TestMethod]
        public void Aplicar_LosMejoresDetrasDelChequeConTopeYSinRepetir()
        {
            var lista = new List<SugerenciaContactoDTO> { Sugerida("C", OrigenesSugerenciaContacto.CHEQUE_REGALO), Sugerida("P1"), Sugerida("A"), Sugerida("P2") };
            var conOfertas = new[] { Con("A", 0.5), Con("B", 0.9), Con("D", 0.7), Con("E", 0.99, leToca: false), Con("F", 0.95, excluido: true), Con("C", 2) }
                .ToDictionary(c => c.Cliente.Clave, c => c);

            List<SugerenciaContactoDTO> resultado = ReglasImpulsoOfertasContacto.Aplicar(lista, conOfertas, HOY, tope: 2);

            CollectionAssert.AreEqual(new[] { "C", "B", "D", "P1", "A", "P2" }, resultado.Select(s => s.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, resultado.Select(s => s.Orden).ToArray());
            Assert.AreEqual(OrigenesSugerenciaContacto.CHEQUE_REGALO, resultado[0].Origen, "al del cheque no se le toca");
            Assert.AreEqual(OrigenesSugerenciaContacto.OFERTAS, resultado[1].Origen);
            Assert.IsTrue(resultado[1].Motivo.StartsWith(OrigenesSugerenciaContacto.PREFIJO_MOTIVO_OFERTAS));
            Assert.IsNotNull(resultado[1].ProductosOferta);
            Assert.AreEqual(OrigenesSugerenciaContacto.OFERTAS, OrigenesSugerenciaContacto.DeMotivo(resultado[1].Motivo));
        }

        [TestMethod]
        public void Aplicar_QuienYaEstabaEnLaListaSubeYNoSaleDosVeces()
        {
            var lista = new List<SugerenciaContactoDTO> { Sugerida("P1"), Sugerida("A") };
            var conOfertas = new[] { Con("A", 0.5) }.ToDictionary(c => c.Cliente.Clave, c => c);

            List<SugerenciaContactoDTO> resultado = ReglasImpulsoOfertasContacto.Aplicar(lista, conOfertas, HOY);

            CollectionAssert.AreEqual(new[] { "A", "P1" }, resultado.Select(s => s.Cliente).ToArray());
        }

        [TestMethod]
        public void Aplicar_SinOfertasLaListaNoCambia()
        {
            var lista = new List<SugerenciaContactoDTO> { Sugerida("P1"), Sugerida("P2") };

            List<SugerenciaContactoDTO> resultado = ReglasImpulsoOfertasContacto.Aplicar(lista, new Dictionary<string, ClienteConOfertas>(), HOY);

            CollectionAssert.AreEqual(new[] { "P1", "P2" }, resultado.Select(s => s.Cliente).ToArray());
        }

        [TestMethod]
        public void Sugerencia_PrioridadDeSiempreOAltaSiHoyNoLeTocaba()
        {
            ClienteConOfertas dentroDeCadencia = Con("A", 1);
            dentroDeCadencia.Cliente.UltimoContacto = HOY.AddDays(-20);

            Assert.AreEqual(PrioridadesContacto.ALTA, ReglasImpulsoOfertasContacto.Sugerencia(dentroDeCadencia, HOY).Prioridad);
        }

        // ---------------- Textos ----------------

        [TestMethod]
        public void Motivo_ElDeLaIssueConFechaDeFin()
        {
            var con = new ClienteConOfertas
            {
                Cliente = Cliente("1001"),
                Productos = new List<ProductoEnOfertaCliente>
                {
                    new ProductoEnOfertaCliente { Producto = "45929", Nombre = "CREMA  HIDRATANTE FACIAL 50 ML", CicloDias = 45, DiasDesdeUltima = 64, LeToca = true,
                        Descuento = 0.2m, TextoOferta = "un 20 % de descuento", FechaHasta = new DateTime(2026, 11, 30) }
                }
            };
            con.Cliente.UltimoContacto = new DateTime(2026, 10, 2);

            Assert.AreEqual("Compra el 45929 (CREMA HIDRATANTE FACIAL) cada 45 días y lleva 64 sin comprarlo; hoy tiene un 20 % de descuento (hasta el 30/11). " +
                "No hablamos con él desde el 02/10.", ReglasImpulsoOfertasContacto.Motivo(con, HOY));
        }

        [TestMethod]
        public void Motivo_NmasMQueCaducaYOtrosProductosSinContacto()
        {
            var con = new ClienteConOfertas
            {
                Cliente = Cliente("1001", diasContacto: null),
                Productos = new List<ProductoEnOfertaCliente>
                {
                    new ProductoEnOfertaCliente { Producto = "45930", Nombre = "PACK", CicloDias = 30, DiasDesdeUltima = 31, LeToca = true,
                        TextoOferta = "el 6+1", CaducaPronto = true, FechaHasta = new DateTime(2026, 11, 20) },
                    new ProductoEnOfertaCliente { Producto = "2", LeToca = true },
                    new ProductoEnOfertaCliente { Producto = "3", LeToca = false }
                }
            };

            Assert.AreEqual("Compra el 45930 (PACK) cada 30 días y lleva 31 sin comprarlo; hoy tiene el 6+1 (solo hasta el 20/11). " +
                "No consta ningún contacto con él. Tiene otro producto suyo en oferta.", ReglasImpulsoOfertasContacto.Motivo(con, HOY));
        }

        [TestMethod]
        public void TextoVigencia_NuevaHastaONada()
        {
            Assert.AreEqual(" (oferta nueva, desde el 12/11)", ReglasImpulsoOfertasContacto.TextoVigencia(
                new ProductoEnOfertaCliente { RecienEntrada = true, FechaDesde = new DateTime(2026, 11, 12), FechaHasta = new DateTime(2026, 12, 31) }, HOY));
            Assert.AreEqual(" (hasta el 31/01/2027)", ReglasImpulsoOfertasContacto.TextoVigencia(
                new ProductoEnOfertaCliente { FechaHasta = new DateTime(2027, 1, 31) }, HOY));
            Assert.AreEqual(string.Empty, ReglasImpulsoOfertasContacto.TextoVigencia(new ProductoEnOfertaCliente(), HOY));
        }

        [TestMethod]
        public void Motivo_NuncaPasaDeLaLongitudMaxima()
        {
            var con = new ClienteConOfertas
            {
                Cliente = Cliente("1001"),
                Productos = new List<ProductoEnOfertaCliente>
                {
                    new ProductoEnOfertaCliente { Producto = "ABCDEFGHIJKLMNO", Nombre = new string('X', 80), CicloDias = 365, DiasDesdeUltima = 1000, LeToca = true,
                        TextoOferta = "un 100 % de descuento", RecienEntrada = true, FechaDesde = new DateTime(2025, 11, 12) },
                    new ProductoEnOfertaCliente { Producto = "2", LeToca = true },
                    new ProductoEnOfertaCliente { Producto = "3", LeToca = true }
                }
            };
            con.Cliente.UltimoContacto = new DateTime(2024, 1, 1);

            string motivo = ReglasImpulsoOfertasContacto.Motivo(con, HOY);

            Assert.IsTrue(motivo.Length <= MotorSugerenciasContacto.LONGITUD_MAXIMA_MOTIVO, motivo);
            StringAssert.StartsWith(motivo, "Compra el ABCDEFGHIJKLMNO");
        }

        [TestMethod]
        public void NombreCorto_PorPalabraEntera()
        {
            Assert.AreEqual("CREMA HIDRATANTE FACIAL", ReglasImpulsoOfertasContacto.NombreCorto("CREMA HIDRATANTE FACIAL 50 ML", 25));
            Assert.AreEqual("CREMA", ReglasImpulsoOfertasContacto.NombreCorto("CREMA HIDRATANTE", 12));
            Assert.AreEqual("SUPERCALIFRA", ReglasImpulsoOfertasContacto.NombreCorto("SUPERCALIFRAGILISTICO", 12));
            Assert.IsNull(ReglasImpulsoOfertasContacto.NombreCorto("CREMA", 0));
        }

        [TestMethod]
        public void ProductosJson_LosTresMejoresEnCorto()
        {
            var con = new ClienteConOfertas
            {
                Cliente = Cliente("1001"),
                Productos = Enumerable.Range(1, 4).Select(i => new ProductoEnOfertaCliente
                {
                    Producto = "P" + i, Descuento = 0.2m, CicloDias = 45, DiasDesdeUltima = 64, LeToca = i == 1, RecienEntrada = i == 2, CaducaPronto = i == 3
                }).ToList()
            };

            Assert.AreEqual("[{\"p\":\"P1\",\"d\":20,\"c\":45,\"u\":64,\"t\":1,\"n\":0,\"f\":0},{\"p\":\"P2\",\"d\":20,\"c\":45,\"u\":64,\"t\":0,\"n\":1,\"f\":0}," +
                "{\"p\":\"P3\",\"d\":20,\"c\":45,\"u\":64,\"t\":0,\"n\":0,\"f\":1}]", ReglasImpulsoOfertasContacto.ProductosJson(con));
            Assert.IsNull(ReglasImpulsoOfertasContacto.ProductosJson(new ClienteConOfertas()));
        }

        [TestMethod]
        public void DeMotivo_LasDeOfertasNoSeConfundenConLaMaxima()
        {
            Assert.AreEqual(OrigenesSugerenciaContacto.OFERTAS, OrigenesSugerenciaContacto.DeMotivo("Compra el 45929 cada 45 días…"));
            Assert.AreEqual(OrigenesSugerenciaContacto.PROBABILIDAD, OrigenesSugerenciaContacto.DeMotivo("Compra cada 12 días y lleva 20 días sin hablar con usted"));
        }

        // ---------------- Lector ----------------

        [TestMethod]
        public async System.Threading.Tasks.Task ImpulsoSql_Apagado_NoLeeOfertas()
        {
            bool leidas = false;
            var impulso = new ImpulsoOfertasContactoSql(() => false, d => { leidas = true; return new Dictionary<string, OfertaProductoDia>(); });

            Dictionary<string, ClienteConOfertas> resultado = await impulso.Leer("MPP", new List<ClienteCarteraContacto> { Cliente("1") }, HOY);

            Assert.AreEqual(0, resultado.Count);
            Assert.IsFalse(leidas);
        }

        [TestMethod]
        public async System.Threading.Tasks.Task ImpulsoSql_SinOfertasHoy_NoConsultaCompras()
        {
            var impulso = new ImpulsoOfertasContactoSql(() => true, d => new Dictionary<string, OfertaProductoDia>());

            Dictionary<string, ClienteConOfertas> resultado = await impulso.Leer("MPP", new List<ClienteCarteraContacto> { Cliente("1") }, HOY);

            Assert.AreEqual(0, resultado.Count);
        }

        [TestMethod]
        public void XmlProductos_EscapadoYSinVacios()
        {
            Assert.AreEqual("<o><p>45929</p><p>A&amp;B</p></o>", ImpulsoOfertasContactoSql.XmlProductos(new[] { " 45929 ", "", "A&B" }));
        }

        [TestMethod]
        public void SqlCompras_MismaAsignacionYFiltroDeGrupoQueLaCartera()
        {
            StringAssert.Contains(ImpulsoOfertasContactoSql.SQL_COMPRAS, RepositorioCarteraContactoSql.SQL_ASIGNACION);
            StringAssert.Contains(ImpulsoOfertasContactoSql.SQL_COMPRAS, RepositorioCarteraContactoSql.FILTRO_GRUPO_LINEA);
            StringAssert.Contains(ImpulsoOfertasContactoSql.SQL_COMPRAS, "@Productos.nodes('/o/p')");
            StringAssert.Contains(ImpulsoOfertasContactoSql.SQL_COMPRAS, "l.SubGrupo <> 'MMP'");
        }
    }
}
