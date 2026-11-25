using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Campanas.OfertasParaTi;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;

namespace NestoAPI.Tests.Infrastructure.Campanas
{
    [TestClass]
    public class PushCampanaOfertasTests
    {
        private const string BF = "Black Friday 2026";

        private static OfertaElegida Oferta(string producto, string nombre, decimal descuento = 0.30m, bool habitual = false, int cantidad = 1)
            => new OfertaElegida
            {
                Candidata = new CandidataOferta { Producto = producto, Nombre = nombre, Descuento = descuento, Campana = BF, Familia = "F" + producto },
                Habitual = habitual,
                CantidadSugerida = cantidad
            };

        private static List<OfertaElegida> Cinco() => new List<OfertaElegida>
        {
            Oferta("45929", "SICUREZZA BB CREAM"),
            Oferta("100", "JABON ISDRUC"),
            Oferta("101", "A"), Oferta("102", "B"), Oferta("103", "C")
        };

        // ---------------------------------------------------------------- textos

        [TestMethod]
        public void CuerpoAutomatico_TresOMas()
        {
            Assert.AreEqual("Sicurezza BB Cream (−30 %), Jabon Isdruc y 3 más pensados para ti.", TextosPushOfertas.CuerpoAutomatico(Cinco()));
        }

        [TestMethod]
        public void CuerpoAutomatico_UnoYDos()
        {
            Assert.AreEqual("Sicurezza BB Cream (−30 %), pensado para ti.", TextosPushOfertas.CuerpoAutomatico(Cinco().Take(1).ToList()));
            Assert.AreEqual("Sicurezza BB Cream (−30 %) y Jabon Isdruc, pensados para ti.", TextosPushOfertas.CuerpoAutomatico(Cinco().Take(2).ToList()));
            Assert.IsNull(TextosPushOfertas.CuerpoAutomatico(new List<OfertaElegida>()));
        }

        [TestMethod]
        public void NombreCorto_RecortaPorPalabra()
        {
            Assert.AreEqual("Crema Hidratante Intensiva", TextosPushOfertas.NombreCorto("CREMA HIDRATANTE INTENSIVA CON ÁCIDO HIALURÓNICO 50 ML"));
            Assert.IsTrue(TextosPushOfertas.NombreCorto("CREMA HIDRATANTE INTENSIVA CON ÁCIDO").Length <= TextosPushOfertas.LONGITUD_NOMBRE);
        }

        [TestMethod]
        public void Plantilla_ConLosMarcadores()
        {
            string texto = TextosPushOfertas.AplicarPlantilla("{Campana}: {producto} con un {descuento} de descuento y {n} más", Cinco(), "Black Friday");
            Assert.AreEqual("Black Friday: Sicurezza BB Cream con un 30 % de descuento y 5 más", texto);
        }

        [TestMethod]
        public void Plantilla_SinProductos_SinEspaciosDeMas()
        {
            Assert.AreEqual("Ofertas de Black Friday, entra ya.",
                TextosPushOfertas.AplicarPlantilla("Ofertas de {campana} {producto}, entra ya.", new List<OfertaElegida>(), "Black Friday"));
        }

        [TestMethod]
        public void Notificacion_ModoOfertas_TipoOfertasConCampanaProductosEImagen()
        {
            NotificacionPushDTO n = TextosPushOfertas.Notificacion(ModosPushCampana.OFERTAS, BF, "Black Friday", "texto",
                Cinco().Take(2).ToList(), "https://tienda.nuevavision.es/img/1.jpg");
            Assert.AreEqual("ofertas", n.Tipo);
            Assert.AreEqual("ofertas", n.Datos["tipo"]);
            Assert.AreEqual(BF, n.Datos["campana"]);
            Assert.AreEqual("45929:1;100:1", n.Datos["productos"]);
            Assert.AreEqual("https://tienda.nuevavision.es/img/1.jpg", n.Datos["imagenUrl"]);
        }

        [TestMethod]
        public void Notificacion_ModoCarrito_TipoReposicionConCantidades_YSinProductosVuelveAOfertas()
        {
            NotificacionPushDTO n = TextosPushOfertas.Notificacion(ModosPushCampana.CARRITO, BF, "t", "c",
                new List<OfertaElegida> { Oferta("1", "X", habitual: true, cantidad: 3) }, "https://");
            Assert.AreEqual("reposicion", n.Datos["tipo"]);
            Assert.AreEqual("1:3", n.Datos["productos"]);
            Assert.IsFalse(n.Datos.ContainsKey("imagenUrl"));

            NotificacionPushDTO vacia = TextosPushOfertas.Notificacion(ModosPushCampana.CARRITO, BF, "t", "c", new List<OfertaElegida>(), null);
            Assert.AreEqual("ofertas", vacia.Datos["tipo"]);
            Assert.IsFalse(vacia.Datos.ContainsKey("productos"));
        }

        // ---------------------------------------------------------------- reglas de la push

        [TestMethod]
        public void Validar_ValoresPorDefectoYErrores()
        {
            Assert.AreEqual(ReglaPushCampana.MENSAJE_SIN_CAMPANA, ReglaPushCampana.Validar(new PeticionCampanaPushDTO(), out _));
            Assert.IsNotNull(ReglaPushCampana.Validar(new PeticionCampanaPushDTO { Campana = BF, Modo = "Otro" }, out _));
            Assert.IsNotNull(ReglaPushCampana.Validar(new PeticionCampanaPushDTO { Campana = BF, Maximo = 11 }, out _));
            Assert.IsNotNull(ReglaPushCampana.Validar(new PeticionCampanaPushDTO { Campana = BF, DiasSinRepetir = -1 }, out _));
            Assert.IsNotNull(ReglaPushCampana.Validar(new PeticionCampanaPushDTO { Campana = BF, ImagenUrl = "foto.jpg" }, out _));

            Assert.IsNull(ReglaPushCampana.Validar(new PeticionCampanaPushDTO { Campana = " " + BF + " ", Modo = "carrito" }, out PeticionPushCampanaValida v));
            Assert.AreEqual(BF, v.Campana);
            Assert.AreEqual(ModosPushCampana.CARRITO, v.Modo);
            Assert.AreEqual(5, v.Maximo);
            Assert.AreEqual(2, v.DiasSinRepetir);
            Assert.IsTrue(v.IncluirSinOfertas);
        }

        [TestMethod]
        public void DesdeSinRepetir_ConDosDias_CuentaLaDeAyer()
        {
            Assert.AreEqual(new DateTime(2026, 11, 25), ReglaPushCampana.DesdeSinRepetir(new DateTime(2026, 11, 26, 10, 0, 0), 2));
            Assert.AreEqual(new DateTime(2026, 11, 26), ReglaPushCampana.DesdeSinRepetir(new DateTime(2026, 11, 26), 1));
            Assert.IsNull(ReglaPushCampana.DesdeSinRepetir(new DateTime(2026, 11, 26), 0));
        }

        [TestMethod]
        public void MarcaCampana_ComoLaEscribeElBuzon()
        {
            string datos = Newtonsoft.Json.JsonConvert.SerializeObject(new Dictionary<string, string> { { "tipo", "ofertas" }, { "campana", "Black \"Friday\" 2026" } });
            Assert.IsTrue(datos.Contains(ReglaPushCampana.MarcaCampana("Black \"Friday\" 2026")));
        }

        [TestMethod]
        public void Repartir_BajasExcluidosRecientesYSoloClientes()
        {
            ReglaPushCampana.Validar(new PeticionCampanaPushDTO
            {
                Campana = BF,
                SoloClientes = new List<string> { "1", "2", "3", "4", "9" },
                ExcluirClientes = new List<string> { "2" }
            }, out PeticionPushCampanaValida p);

            ReglaPushCampana.Reparto r = ReglaPushCampana.Repartir(new[] { "1", "2", "3", "4", "5" }, new HashSet<string> { "3" }, p, new HashSet<string> { "4" });

            CollectionAssert.AreEqual(new[] { "1" }, r.Destinatarios);
            Assert.AreEqual(ReglaPushCampana.MOTIVO_SIN_APP, r.Saltados.Single(s => s.Cliente == "9").Motivo);
            Assert.AreEqual(ReglaPushCampana.MOTIVO_EXCLUIDO, r.Saltados.Single(s => s.Cliente == "2").Motivo);
            Assert.AreEqual(ReglaPushCampana.MOTIVO_DE_BAJA, r.Saltados.Single(s => s.Cliente == "3").Motivo);
            StringAssert.Contains(r.Saltados.Single(s => s.Cliente == "4").Motivo, "Ya recibió");
            Assert.IsFalse(r.Saltados.Any(s => s.Cliente == "5"));
        }

        [TestMethod]
        public void ProductosDelModo_CarritoSoloHabituales()
        {
            var lista = new List<OfertaElegida> { Oferta("1", "A", habitual: true), Oferta("2", "B") };
            CollectionAssert.AreEqual(new[] { "1" }, ReglaPushCampana.ProductosDelModo(ModosPushCampana.CARRITO, lista).Select(o => o.Candidata.Producto).ToList());
            Assert.AreEqual(2, ReglaPushCampana.ProductosDelModo(ModosPushCampana.OFERTAS, lista).Count);
        }

        [TestMethod]
        public void PuedeLanzar_SoloDireccionEInformatica()
        {
            Assert.IsTrue(ReglaPushCampana.PuedeLanzar(new GenericPrincipal(new GenericIdentity("Carlos"), new[] { "Dirección" })));
            Assert.IsTrue(ReglaPushCampana.PuedeLanzar(new GenericPrincipal(new GenericIdentity("Carlos"), new[] { @"NUEVAVISION\Informática" })));
            Assert.IsFalse(ReglaPushCampana.PuedeLanzar(new GenericPrincipal(new GenericIdentity("Laura"), new[] { "Administración" })));
            Assert.IsFalse(ReglaPushCampana.PuedeLanzar(null));
        }

        [TestMethod]
        public void EnHorario_De9a21()
        {
            Assert.IsTrue(ReglaPushCampana.EnHorario(new DateTime(2026, 11, 26, 9, 0, 0)));
            Assert.IsFalse(ReglaPushCampana.EnHorario(new DateTime(2026, 11, 26, 21, 0, 0)));
            Assert.IsFalse(ReglaPushCampana.EnHorario(new DateTime(2026, 11, 26, 8, 59, 0)));
        }

        // ---------------------------------------------------------------- precios

        [TestMethod]
        public void Precios_SoloSiMejora()
        {
            Assert.IsTrue(ReglaPreciosOfertaParaTi.Mejora(new PreciosOferta { Precio = 8.75m, PrecioAntes = 12.5m }));
            Assert.IsFalse(ReglaPreciosOfertaParaTi.Mejora(new PreciosOferta { Precio = 8.75m, PrecioAntes = 8.75m }));
            Assert.IsFalse(ReglaPreciosOfertaParaTi.Mejora(new PreciosOferta { Precio = 9m, PrecioAntes = 8m }));
            Assert.IsFalse(ReglaPreciosOfertaParaTi.Mejora(null));
            Assert.AreEqual(0.30m, ReglaPreciosOfertaParaTi.Descuento(new PreciosOferta { Precio = 8.75m, PrecioAntes = 12.5m }));
        }

        [TestMethod]
        public void Precios_PoliticaDePreciosOcultos()
        {
            var precios = new PreciosOferta { Precio = 8.751m, PrecioAntes = 12.5m, Tarifa = 15m };

            var completo = new OfertaParaTiDTO();
            ReglaPreciosOfertaParaTi.AplicarNivel(completo, precios, PoliticaPreciosOcultos.NivelPrecios.Completo);
            Assert.AreEqual(8.75m, completo.Precio);
            Assert.AreEqual(12.5m, completo.PrecioAntes);
            Assert.AreEqual(0.2999m, completo.Descuento);

            var sinDescuentos = new OfertaParaTiDTO();
            ReglaPreciosOfertaParaTi.AplicarNivel(sinDescuentos, precios, PoliticaPreciosOcultos.NivelPrecios.SinDescuentos);
            Assert.AreEqual(15m, sinDescuentos.Precio);
            Assert.IsNull(sinDescuentos.PrecioAntes);
            Assert.IsNull(sinDescuentos.Descuento);

            var sinPrecios = new OfertaParaTiDTO();
            ReglaPreciosOfertaParaTi.AplicarNivel(sinPrecios, precios, PoliticaPreciosOcultos.NivelPrecios.SinPrecios);
            Assert.IsNull(sinPrecios.Precio);
            Assert.IsNull(sinPrecios.PrecioAntes);
            Assert.IsNull(sinPrecios.Descuento);
        }
    }
}
