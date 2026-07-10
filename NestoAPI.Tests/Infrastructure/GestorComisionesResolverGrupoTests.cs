using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#249: grupo comisionable de los productos marcados (guantes de nitrilo COS/PEL).
    /// Regla simétrica: la línea se convierte al grupo por el que comisiona quien mete el pedido,
    /// SALVO que el grupo de ficha esté protegido por un vendedor REAL del cliente (solo se puede
    /// "pisar" un grupo sin registro, con vendedor en blanco o con el genérico NV). Si el pedido no
    /// lo mete ningún vendedor del cliente, se queda el grupo de la ficha.
    /// Cliente de ejemplo: cabecera JE (Jesús), vendedor de peluquería IF (Israel).
    /// </summary>
    [TestClass]
    public class GestorComisionesResolverGrupoTests
    {
        private const string COS = "COS";
        private const string PEL = "PEL";
        private const string JE = "JE";
        private const string IF = "IF";
        private const string NV = "NV";
        private static readonly List<string> ALTERNATIVO_PEL = new List<string> { PEL };
        private static readonly List<string> ALTERNATIVO_COS = new List<string> { COS };

        private static Dictionary<string, string> VendedorPeluqueria(string vendedor)
            => new Dictionary<string, string> { { PEL, vendedor } };

        [TestMethod]
        public void FichaCos_LoMeteElDeCabecera_QuedaCos()
        {
            string grupo = GestorComisiones.ResolverGrupoComisionable(COS, ALTERNATIVO_PEL, VendedorPeluqueria(IF), JE, JE);

            Assert.AreEqual(COS, grupo, "El grupo de ficha ya comisiona a quien mete el pedido");
        }

        [TestMethod]
        public void FichaCos_LoMeteElDePeluqueria_ConvierteAPel()
        {
            string grupo = GestorComisiones.ResolverGrupoComisionable(COS, ALTERNATIVO_PEL, VendedorPeluqueria(IF), JE, IF);

            Assert.AreEqual(PEL, grupo, "COS no tiene registro propio (comisiona cabecera): se puede pisar y la línea pasa a PEL");
        }

        [TestMethod]
        public void FichaPel_LoMeteElDePeluqueria_QuedaPel()
        {
            string grupo = GestorComisiones.ResolverGrupoComisionable(PEL, ALTERNATIVO_COS, VendedorPeluqueria(IF), JE, IF);

            Assert.AreEqual(PEL, grupo);
        }

        [TestMethod]
        public void FichaPel_LoMeteElDeCabecera_ConVendedorRealDePel_QuedaPel()
        {
            string grupo = GestorComisiones.ResolverGrupoComisionable(PEL, ALTERNATIVO_COS, VendedorPeluqueria(IF), JE, JE);

            Assert.AreEqual(PEL, grupo, "PEL está protegido por un vendedor real (IF): no se le puede quitar la línea");
        }

        [TestMethod]
        public void FichaPel_LoMeteElDeCabecera_ConVendedorNvDePel_ConvierteACos()
        {
            string grupo = GestorComisiones.ResolverGrupoComisionable(PEL, ALTERNATIVO_COS, VendedorPeluqueria(NV), JE, JE);

            Assert.AreEqual(COS, grupo, "Un registro NV sí se puede pisar: la línea pasa a COS y comisiona el de cabecera");
        }

        [TestMethod]
        public void FichaPel_LoMeteElDeCabecera_SinRegistroDePel_NoHaceFaltaConvertir()
        {
            // Sin registro de PEL, el comisionista de PEL ya es el de cabecera: la línea se queda en
            // PEL y comisiona él igualmente. La conversión a COS solo hace falta cuando el registro
            // NV existe y desviaría la comisión a NV.
            string grupo = GestorComisiones.ResolverGrupoComisionable(PEL, ALTERNATIVO_COS, new Dictionary<string, string>(), JE, JE);

            Assert.AreEqual(PEL, grupo);
        }

        [TestMethod]
        public void LoMeteUnTercero_QuedaLaFicha()
        {
            string grupo = GestorComisiones.ResolverGrupoComisionable(COS, ALTERNATIVO_PEL, VendedorPeluqueria(IF), JE, "MR");

            Assert.AreEqual(COS, grupo, "Quien no es vendedor del cliente (oficina, web, tercero) no provoca conversión");
        }

        [TestMethod]
        public void UsuarioSinVendedor_QuedaLaFicha()
        {
            string grupo = GestorComisiones.ResolverGrupoComisionable(COS, ALTERNATIVO_PEL, VendedorPeluqueria(IF), JE, null);

            Assert.AreEqual(COS, grupo);
        }

        [TestMethod]
        public void UsuarioConVendedorNv_NoReclamaNada()
        {
            // Un administrativo con vendedor NV no debe arrastrar la línea hacia un registro NV del cliente.
            string grupo = GestorComisiones.ResolverGrupoComisionable(COS, ALTERNATIVO_PEL, VendedorPeluqueria(NV), JE, NV);

            Assert.AreEqual(COS, grupo);
        }

        [TestMethod]
        public void ProductoSinAlternativos_QuedaLaFicha()
        {
            string grupo = GestorComisiones.ResolverGrupoComisionable(COS, new List<string>(), VendedorPeluqueria(IF), JE, IF);

            Assert.AreEqual(COS, grupo, "Un producto sin marcar nunca cambia de grupo");
        }

        [TestMethod]
        public void ValoresConPadding_SeComparanConTrim()
        {
            // Los char(3)/char(15) de la BD vienen con espacios de relleno.
            var vendedores = new Dictionary<string, string> { { PEL, "IF " } };
            string grupo = GestorComisiones.ResolverGrupoComisionable("COS", new List<string> { "PEL " }, vendedores, "JE ", "IF ");

            Assert.AreEqual(PEL, grupo);
        }

        [TestMethod]
        public void VariosCandidatos_GanaElPrimeroPorElQueComisiona()
        {
            // Desempate documentado: orden de alta en la tabla.
            var vendedores = new Dictionary<string, string> { { PEL, IF }, { "ACC", IF } };
            string grupo = GestorComisiones.ResolverGrupoComisionable(COS, new List<string> { "ACC", PEL }, vendedores, JE, IF);

            Assert.AreEqual("ACC", grupo);
        }
    }
}
