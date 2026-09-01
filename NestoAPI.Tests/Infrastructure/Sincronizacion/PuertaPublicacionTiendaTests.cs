using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Sincronizacion;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#432: las reglas de la puerta de publicación, una a una. Son la transcripción de la
    /// consulta legacy (Scripts/Legacy_ConsultaPrestashopConClasificacion.sql) más las decisiones
    /// del 31/08-01/09/26; si un test molesta al cambiar una regla, lo que está cambiando es una
    /// decisión de negocio y debe cambiarse a la vez aquí y en la puerta.
    /// </summary>
    [TestClass]
    public class PuertaPublicacionTiendaTests
    {
        /// <summary>Un producto corriente que pasa la puerta; cada test rompe lo suyo.</summary>
        private static DatosPuertaPublicacion ProductoNormal()
        {
            return new DatosPuertaPublicacion
            {
                Numero = "12345",
                Grupo = "COS",
                Subgrupo = "001",
                FamiliaCodigo = "Fama",
                DescripcionFamilia = "Fama Fabré",
                DescripcionSubgrupo = "Cosmética facial",
                Ficticio = false,
                Estado = 0,
                TieneProveedorPrincipalValido = true,
                EsParteDeKitActivo = false,
                TieneMovimientoExtractoTresAnnos = false
            };
        }

        [TestMethod]
        public void Evaluar_ProductoNormalEstadoCero_Publicable()
        {
            var resultado = PuertaPublicacionTienda.Evaluar(ProductoNormal());

            Assert.IsTrue(resultado.Publicable);
            Assert.IsNull(resultado.Motivo);
        }

        [TestMethod]
        public void Evaluar_Ficticio_NoPublicable()
        {
            var datos = ProductoNormal();
            datos.Ficticio = true;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsFalse(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_FicticioDeBaja_NoPublicable()
        {
            // La baja publica para que la tienda desactive, pero un ficticio nunca debió entrar:
            // publicarlo de baja podría CREARLO (inactivo) en la tienda. La identidad manda.
            var datos = ProductoNormal();
            datos.Ficticio = true;
            datos.Estado = -1;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsFalse(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_ReferenciasVetadas_NoPublicablesYConSuPorque()
        {
            var casos = new[]
            {
                ("36486", "cortapuntas"),
                ("37152", "Starsoft"),
                ("32755", "Química Alemana"),
                ("22072", "cartílago"),
                ("24211", "cartílago")
            };

            foreach ((string numero, string trozoDeMotivo) in casos)
            {
                var datos = ProductoNormal();
                datos.Numero = numero;

                var resultado = PuertaPublicacionTienda.Evaluar(datos);

                Assert.IsFalse(resultado.Publicable, $"El {numero} debería estar vetado");
                StringAssert.Contains(resultado.Motivo, trozoDeMotivo);
            }
        }

        [TestMethod]
        public void Evaluar_GrupoMtp_NoPublicable()
        {
            var datos = ProductoNormal();
            datos.Grupo = "MTP";

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsFalse(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_RecambioAparatologiaSinMovimientoEnTresAnnos_NoPublicable()
        {
            var datos = ProductoNormal();
            datos.Grupo = "APA";
            datos.Subgrupo = "010";
            datos.TieneMovimientoExtractoTresAnnos = false;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsFalse(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_RecambioAparatologiaConMovimiento_Publicable()
        {
            // La decisión del 31/08/26: los recambios ya no se excluyen en bloque como en el
            // legacy; entra lo que se mueve.
            var datos = ProductoNormal();
            datos.Grupo = "APA";
            datos.Subgrupo = "010";
            datos.TieneMovimientoExtractoTresAnnos = true;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsTrue(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_MaystarEnOtrosAparatos_NoPublicable()
        {
            var datos = ProductoNormal();
            datos.DescripcionFamilia = "Maystar";
            datos.DescripcionSubgrupo = "Otros aparatos";

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsFalse(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_MaystarFueraDeOtrosAparatos_Publicable()
        {
            var datos = ProductoNormal();
            datos.DescripcionFamilia = "Maystar";
            datos.DescripcionSubgrupo = "Cosmética facial";

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsTrue(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_ProductoDeBaja_PublicableParaQueLaTiendaLoDesactive()
        {
            // prestashop-nestosync#8: el mensaje de un producto con Estado < 0 es lo que desactiva
            // el producto en la tienda. Si la puerta lo frenara, la baja nunca llegaría.
            var datos = ProductoNormal();
            datos.Estado = -1;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsTrue(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_ConProveedoresPeroNingunoConOrdenUno_NoPublicable()
        {
            var datos = ProductoNormal();
            datos.TieneProveedorPrincipalValido = false;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsFalse(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_EstadosDeLaMatrizGeneral_Publicables()
        {
            foreach (short estado in new short[] { 0, 1, 4 })
            {
                var datos = ProductoNormal();
                datos.Estado = estado;

                Assert.IsTrue(PuertaPublicacionTienda.Evaluar(datos).Publicable,
                    $"El estado {estado} debería ser publicable");
            }
        }

        [TestMethod]
        public void Evaluar_EstadoFueraDeLaMatriz_NoPublicable()
        {
            var datos = ProductoNormal();
            datos.Estado = 2;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsFalse(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_LOrealEstadoSiete_Publicable()
        {
            var datos = ProductoNormal();
            datos.FamiliaCodigo = "LOréal";
            datos.Estado = 7;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsTrue(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_SchwarzkopfCualquierEstadoPositivo_Publicable()
        {
            // El legacy escribe 'Schwarzkop' (el código de familia, char truncado).
            var datos = ProductoNormal();
            datos.FamiliaCodigo = "Schwarzkop";
            datos.Estado = 9;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsTrue(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_EssieEstadoTres_Publicable()
        {
            var datos = ProductoNormal();
            datos.FamiliaCodigo = "Essie";
            datos.Estado = 3;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsTrue(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_FamiliaFueraDeLaListaConEstadoTres_NoPublicable()
        {
            // El estado 3 solo entra para las familias de la lista del legacy (01/03/17); una que
            // no esté en ella no entra.
            var datos = ProductoNormal();
            datos.FamiliaCodigo = "Genérica";
            datos.Estado = 3;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsFalse(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_ReferenciaQueEntraSiempre_PublicableConEstadoRaro()
        {
            var datos = ProductoNormal();
            datos.Numero = "32819";
            datos.Estado = 9;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsTrue(resultado.Publicable);
        }

        [TestMethod]
        public void Evaluar_AsociadoDeKitActivo_PublicableAunqueSuEstadoNoPaseLaMatriz()
        {
            var datos = ProductoNormal();
            datos.Estado = 2;
            datos.EsParteDeKitActivo = true;

            var resultado = PuertaPublicacionTienda.Evaluar(datos);

            Assert.IsTrue(resultado.Publicable);
        }
    }
}
