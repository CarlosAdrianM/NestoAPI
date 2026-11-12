using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>NestoAPI#857: asignación en sombra a pedidos de compra en el aire (proveedores y datos INVENTADOS).</summary>
    [TestClass]
    public class CorreosSeguimientoAsignacionTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 12, 10, 0, 0);

        private static List<PedidoCompraEnAire> Pedidos() => new List<PedidoCompraEnAire>
        {
            new PedidoCompraEnAire { Empresa = "1", Pedido = 912001, Proveedor = "9001", NombreProveedor = "COSMÉTICOS INVENTADOS, S.L.",
                Web = "www.cosmeticos-inventados.es", Correos = new List<string> { "pedidos@cosmeticos-inventados.es" }, FechaEnvio = HOY.AddDays(-3) },
            new PedidoCompraEnAire { Empresa = "1", Pedido = 912007, Proveedor = "9001", NombreProveedor = "COSMÉTICOS INVENTADOS, S.L.",
                Web = "www.cosmeticos-inventados.es", Correos = new List<string> { "pedidos@cosmeticos-inventados.es" }, FechaEnvio = HOY.AddDays(-1) },
            new PedidoCompraEnAire { Empresa = "1", Pedido = 912010, Proveedor = "9002", NombreProveedor = "LABORATORIOS DE PRUEBA SA",
                Web = "http://lab-prueba.com/es", Correos = new List<string> { "ana.lopez@gmail.com" }, FechaEnvio = HOY.AddDays(-2) }
        };

        private static CorreoCompras Correo(string texto, string asunto = "Su envío", string remitente = "noreply@gls-spain.es", string nombre = null)
            => new CorreoCompras { Id = "x", Recibido = HOY, Asunto = asunto, Cuerpo = texto, CuerpoEsHtml = false, RemitenteDireccion = remitente, RemitenteNombre = nombre };

        private static AsignacionPedidoCompra Asignar(CorreoCompras correo)
            => AsignadorPedidosCompra.Asignar(correo, TextoCorreo.RemitentesOriginales(correo.Texto), Pedidos());

        [TestMethod]
        public void NumeroDePedidoNuestro_EsElCandidatoUnico()
        {
            AsignacionPedidoCompra asignacion = Asignar(Correo("Referencia del remitente: 912007. Bultos: 2.", asunto: "Envío de COSMÉTICOS INVENTADOS"));

            Assert.AreEqual(912007, asignacion.Candidatos.Single().Pedido);
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_NUMERO, asignacion.Candidatos.Single().Motivo);
            Assert.AreEqual("9001", asignacion.ProveedorPropuesto);
        }

        [TestMethod]
        public void UnNumeroDentroDeUnCodigoMasLargo_NoEsNuestroPedido()
        {
            AsignacionPedidoCompra asignacion = Asignar(Correo("Su envío 9120071234 y 912007123 sale hoy.", remitente: "noreply@gls-spain.es"));

            Assert.AreEqual(0, asignacion.Candidatos.Count);
        }

        // NestoAPI#853 (la asignación escribe en compras): el texto real de MBE del 12/11/26 no da ningún pedido por número aunque haya
        // pedidos en el aire con los números pegados a la referencia ni con los códigos postales.
        [TestMethod]
        public void MBE_NingunCandidatoPorNumero()
        {
            var pedidos = new List<PedidoCompraEnAire>
            {
                new PedidoCompraEnAire { Empresa = "1", Pedido = 252231, Proveedor = "9001", NombreProveedor = "OTRO PROVEEDOR UNO SL" },
                new PedidoCompraEnAire { Empresa = "1", Pedido = 77626, Proveedor = "9002", NombreProveedor = "OTRO PROVEEDOR DOS SL" },
                new PedidoCompraEnAire { Empresa = "1", Pedido = 28110, Proveedor = "9003", NombreProveedor = "OTRO PROVEEDOR TRES SL" },
                new PedidoCompraEnAire { Empresa = "1", Pedido = 8600, Proveedor = "9004", NombreProveedor = "OTRO PROVEEDOR CUATRO SL" }
            };
            CorreoCompras correo = Correo(CorreosSeguimientoFase3Tests.MBE_TEXTO, asunto: "Tus envíos MBE", remitente: "noreply@mbe.es");

            AsignacionPedidoCompra asignacion = AsignadorPedidosCompra.Asignar(correo, TextoCorreo.RemitentesOriginales(correo.Texto), pedidos);

            Assert.AreEqual(0, asignacion.Candidatos.Count(c => c.Motivo == AsignadorPedidosCompra.MOTIVO_NUMERO));
            Assert.IsFalse(AsignadorPedidosCompra.NumerosDePedido(CorreosSeguimientoFase3Tests.MBE_TEXTO).Any(n => n.Numero == 252231 || n.Numero == 77626),
                "Los números pegados a letras de la referencia no son tokens");
        }

        [TestMethod]
        public void SuPedido_ConEtiqueta_EsCandidatoPorNumero()
        {
            var pedidos = new List<PedidoCompraEnAire> { new PedidoCompraEnAire { Empresa = "1", Pedido = 220873, Proveedor = "9009", NombreProveedor = "NADIE QUE SALGA" } };

            AsignacionPedidoCompra asignacion = AsignadorPedidosCompra.Asignar(Correo("Hola, Su pedido 220873 ha salido hoy."), null, pedidos);

            Assert.AreEqual(220873, asignacion.Candidatos.Single().Pedido);
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_NUMERO, asignacion.Candidatos.Single().Motivo);
        }

        [DataTestMethod]
        [DataRow("Nº pedido: 220873", true)]
        [DataRow("Purchase Order No. 220873", true)]
        [DataRow("Ref.: 220873", true)]
        [DataRow("s/ref 220873", true)]
        [DataRow("28110 ALGETE MADRID", false)]
        [DataRow("Peso 220873 g", false)]
        public void Etiqueta_DePedidoDelante(string texto, bool etiquetado)
        {
            Assert.AreEqual(etiquetado, AsignadorPedidosCompra.NumerosDePedido(texto).Single().Etiquetado, texto);
        }

        [TestMethod]
        public void NumeroSuelto_SinOtroCriterio_NoBasta_YConElNombreDelProveedor_Si()
        {
            Assert.AreEqual(0, Asignar(Correo("Bultos: 3. Envío 912010 en reparto.")).Candidatos.Count, "Ni etiqueta ni proveedor reconocido");

            AsignacionPedidoCompra conNombre = Asignar(Correo("LABORATORIOS DE PRUEBA: envío 912010 en reparto."));
            Assert.AreEqual(912010, conNombre.Candidatos.Single().Pedido);
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_NUMERO, conNombre.Candidatos.Single().Motivo);
        }

        [TestMethod]
        public void ProveedorConVariosPedidosEnElAire_TodosSonCandidatosNoUnoInventado()
        {
            AsignacionPedidoCompra asignacion = Asignar(Correo("Remitente: COSMETICOS INVENTADOS SL. Entrega prevista mañana."));

            CollectionAssert.AreEqual(new[] { 912007, 912001 }, asignacion.Candidatos.Select(c => c.Pedido).ToArray(), "Los dos, el más reciente primero");
            Assert.IsTrue(asignacion.Candidatos.All(c => c.Motivo == AsignadorPedidosCompra.MOTIVO_NOMBRE));
            Assert.AreEqual("9001", asignacion.ProveedorPropuesto);
        }

        [TestMethod]
        public void ReenvioDelProveedor_PorLaDireccionDelContacto()
        {
            AsignacionPedidoCompra asignacion = Asignar(Correo(
                "---------- Forwarded message ---------\nFrom: Pedidos <pedidos@cosmeticos-inventados.es>\nDate: hoy\n\nSale con SEUR.",
                remitente: "compras@nuevavision.es"));

            Assert.AreEqual(2, asignacion.Candidatos.Count);
            Assert.IsTrue(asignacion.Candidatos.All(c => c.Motivo == AsignadorPedidosCompra.MOTIVO_CORREO));
        }

        [TestMethod]
        public void PorDominioDeLaWeb()
        {
            AsignacionPedidoCompra asignacion = Asignar(Correo("Su pedido sale hoy.", remitente: "logistica@lab-prueba.com"));

            Assert.AreEqual(912010, asignacion.Candidatos.Single().Pedido);
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_DOMINIO, asignacion.Candidatos.Single().Motivo);
        }

        [TestMethod]
        public void UnDominioGratuito_NoAsignaATodoElQueUseGmail()
        {
            AsignacionPedidoCompra asignacion = Asignar(Correo("Le mando el seguimiento.", remitente: "otra.persona@gmail.com"));

            Assert.AreEqual(0, asignacion.Candidatos.Count);
            Assert.IsNull(asignacion.ProveedorPropuesto);
        }

        [TestMethod]
        public void LaDireccionExactaDeGmailDelContacto_SiCasa()
        {
            Assert.AreEqual(912010, Asignar(Correo("Seguimiento adjunto.", remitente: "Ana.Lopez@gmail.com")).Candidatos.Single().Pedido);
        }

        [TestMethod]
        public void DosProveedoresEnElTexto_CandidatosDeLosDosYSinProveedorPropuesto()
        {
            AsignacionPedidoCompra asignacion = Asignar(Correo("Grupaje: COSMETICOS INVENTADOS y LABORATORIOS DE PRUEBA."));

            Assert.AreEqual(3, asignacion.Candidatos.Count);
            Assert.IsNull(asignacion.ProveedorPropuesto);
        }

        [TestMethod]
        public void SinNadaConQueProponer_SinCandidatos()
        {
            AsignacionPedidoCompra asignacion = Asignar(Correo("Su envío llega mañana."));

            Assert.AreEqual(0, asignacion.Candidatos.Count);
            Assert.IsNull(AsignadorPedidosCompra.Serializar(asignacion.Candidatos));
        }

        [TestMethod]
        public void SinPedidosEnElAire_SinCandidatos()
        {
            Assert.AreEqual(0, AsignadorPedidosCompra.Asignar(Correo("912007"), null, null).Candidatos.Count);
        }

        [TestMethod]
        public void NombreSinSociedad_QuitaLaFormaJuridica()
        {
            Assert.AreEqual("COSMETICOS INVENTADOS", AsignadorPedidosCompra.NombreSinSociedad("Cosméticos Inventados, S.L.U."));
            Assert.AreEqual("LABORATORIOS DE PRUEBA", AsignadorPedidosCompra.NombreSinSociedad("LABORATORIOS DE PRUEBA SA"));
        }

        [TestMethod]
        public void DominioWeb_ConYSinEsquema()
        {
            Assert.AreEqual("cosmeticos-inventados.es", AsignadorPedidosCompra.DominioWeb("www.cosmeticos-inventados.es"));
            Assert.AreEqual("lab-prueba.com", AsignadorPedidosCompra.DominioWeb("http://lab-prueba.com/es"));
            Assert.IsNull(AsignadorPedidosCompra.DominioWeb("   "));
        }

        [TestMethod]
        public void SerializarYDeserializar_IdaYVuelta()
        {
            var candidatos = new List<CandidatoPedidoCompra> { new CandidatoPedidoCompra { Empresa = "1", Pedido = 5, Proveedor = "9", Motivo = "Nombre" } };
            List<CandidatoPedidoCompra> vuelta = AsignadorPedidosCompra.Deserializar(AsignadorPedidosCompra.Serializar(candidatos));
            Assert.AreEqual(5, vuelta.Single().Pedido);
            Assert.AreEqual(0, AsignadorPedidosCompra.Deserializar("no es json").Count);
        }

        #region Medición

        private static List<CandidatoPedidoCompra> Candidatos(params int[] pedidos)
            => pedidos.Select(p => new CandidatoPedidoCompra { Empresa = "1", Pedido = p }).ToList();

        [TestMethod]
        public void DecidirReal_ElPrimeroRecibidoDesdeElDiaDelCorreo()
        {
            var albaranes = new List<AlbaranPedidoCompra>
            {
                new AlbaranPedidoCompra { Empresa = "1", Pedido = 912001, FechaAlbaran = HOY.Date.AddDays(-5) },  // de antes: no
                new AlbaranPedidoCompra { Empresa = "1", Pedido = 912007, FechaAlbaran = HOY.Date.AddDays(2) },
                new AlbaranPedidoCompra { Empresa = "1", Pedido = 912001, FechaAlbaran = HOY.Date.AddDays(1) },
                new AlbaranPedidoCompra { Empresa = "1", Pedido = 999999, FechaAlbaran = HOY.Date }               // no es candidato
            };

            AlbaranPedidoCompra real = AsignadorPedidosCompra.DecidirReal(HOY, Candidatos(912001, 912007), albaranes);

            Assert.AreEqual(912001, real.Pedido);
            Assert.AreEqual(HOY.Date.AddDays(1), real.FechaAlbaran);
        }

        [TestMethod]
        public void DecidirReal_SinAlbaranAun_Null()
        {
            Assert.IsNull(AsignadorPedidosCompra.DecidirReal(HOY, Candidatos(912001), new List<AlbaranPedidoCompra>()));
        }

        [TestMethod]
        public void DecidirReal_OtraEmpresaConElMismoNumero_NoCuenta()
        {
            var albaranes = new List<AlbaranPedidoCompra> { new AlbaranPedidoCompra { Empresa = "3", Pedido = 912001, FechaAlbaran = HOY } };
            Assert.IsNull(AsignadorPedidosCompra.DecidirReal(HOY, Candidatos(912001), albaranes));
        }

        #endregion
    }
}
