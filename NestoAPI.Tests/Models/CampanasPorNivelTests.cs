using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// NestoAPI#423 (Slice 3): una campaña se puede expresar por FAMILIA (una fila para las 62
    /// referencias de la marca) además de producto a producto. En cuanto hay varios niveles, el
    /// criterio viejo de "gana el mayor" deja de valer: hay que replicar la precedencia EXACTA de
    /// <c>GestorPrecios.calcularDescuentoProducto</c>, o la tienda anuncia un porcentaje y Nesto
    /// cobra otro — que es justo lo que #423 viene a arreglar.
    ///
    /// La precedencia del motor para un cliente sin filas propias:
    ///   1. familia          → fija el %
    ///   2. familia + grupo  → SOBRESCRIBE (aunque sea menor)
    ///   3. producto         → gana solo si es MAYOR
    /// </summary>
    [TestClass]
    public class CampanasPorNivelTests
    {
        private const string FAMILIA = "Ufaes";
        private const string GRUPO = "COS";

        private static DescuentosProducto DeProducto(decimal descuento, short cantidadMinima = 1,
            decimal? precio = null, byte ambito = 2)
        {
            return new DescuentosProducto
            {
                Empresa = "1",
                Nº_Producto = "44166",
                CantidadMínima = cantidadMinima,
                Descuento = descuento,
                Precio = precio,
                AudienciaOferta = ambito
            };
        }

        private static DescuentosProducto DeFamilia(decimal descuento, decimal? precio = null, byte ambito = 2)
        {
            return new DescuentosProducto
            {
                Empresa = "1",
                Familia = FAMILIA,
                CantidadMínima = 1,
                Descuento = descuento,
                Precio = precio,
                AudienciaOferta = ambito
            };
        }

        private static DescuentosProducto DeFamiliaYGrupo(decimal descuento, string grupo = GRUPO, byte ambito = 2)
        {
            return new DescuentosProducto
            {
                Empresa = "1",
                Familia = FAMILIA,
                GrupoProducto = grupo,
                CantidadMínima = 1,
                Descuento = descuento,
                AudienciaOferta = ambito
            };
        }

        private static DescuentosPorAudiencia Calcular(params DescuentosProducto[] filas)
        {
            return ProductoDTO.CalcularDescuentosPorAudiencia(filas, 100M, GRUPO);
        }

        // Lo que pide el equipo de PrestaShop: "toda la familia Ufaes al 15 %" en UNA fila.
        [TestMethod]
        public void SoloFamilia_ElProductoHeredaElPorcentajeDeLaMarca()
        {
            DescuentosPorAudiencia resultado = Calcular(DeFamilia(0.15M));

            Assert.AreEqual(15M, resultado.Profesional);
            Assert.AreEqual(15M, resultado.Publico);
        }

        [TestMethod]
        public void FamiliaYProducto_ElProductoGanaSiEsMayor()
        {
            DescuentosPorAudiencia resultado = Calcular(DeFamilia(0.15M), DeProducto(0.30M));

            Assert.AreEqual(30M, resultado.Profesional);
        }

        /// <summary>
        /// EL TEST QUE JUSTIFICA EL SLICE. Con el criterio viejo de "gana el mayor" esto daría 15,
        /// pero el motor de precios cobra 15 solo si el nivel de producto NO es menor: su línea
        /// lleva un `>`, así que un 10 % de producto no pisa un 15 % de familia. La tienda tiene
        /// que anunciar 15, que es lo que Nesto cobra.
        /// </summary>
        [TestMethod]
        public void FamiliaYProducto_ElProductoNoPisaALaFamiliaSiEsMenor()
        {
            DescuentosPorAudiencia resultado = Calcular(DeFamilia(0.15M), DeProducto(0.10M));

            Assert.AreEqual(15M, resultado.Profesional);
        }

        /// <summary>
        /// El nivel familia+grupo es una ASIGNACIÓN en el motor, no una comparación: sobrescribe a
        /// la familia aunque el porcentaje baje. Es lo que permite "toda la marca al 20 %, pero su
        /// línea de cosmética al 10 %".
        /// </summary>
        [TestMethod]
        public void FamiliaYGrupo_SobrescribeALaFamiliaAunqueSeaMenor()
        {
            DescuentosPorAudiencia resultado = Calcular(DeFamilia(0.20M), DeFamiliaYGrupo(0.10M));

            Assert.AreEqual(10M, resultado.Profesional);
        }

        [TestMethod]
        public void FamiliaYGrupoDeOtroGrupo_NoAlcanzaAlProducto()
        {
            DescuentosPorAudiencia resultado = Calcular(DeFamilia(0.20M), DeFamiliaYGrupo(0.10M, grupo: "PEL"));

            Assert.AreEqual(20M, resultado.Profesional);
        }

        // Los tres niveles a la vez, en el orden del motor: 20 → 10 (sobrescribe) → 25 (mayor, gana).
        [TestMethod]
        public void LosTresNiveles_SeAplicanEnElOrdenDelMotor()
        {
            DescuentosPorAudiencia resultado = Calcular(
                DeFamilia(0.20M), DeFamiliaYGrupo(0.10M), DeProducto(0.25M));

            Assert.AreEqual(25M, resultado.Profesional);
        }

        [TestMethod]
        public void LosTresNiveles_ElProductoMenorNoPisaAlDeFamiliaYGrupo()
        {
            DescuentosPorAudiencia resultado = Calcular(
                DeFamilia(0.20M), DeFamiliaYGrupo(0.10M), DeProducto(0.05M));

            Assert.AreEqual(10M, resultado.Profesional);
        }

        /// <summary>
        /// Regresión de la divergencia que encontré al mapear el motor: dentro de un mismo nivel
        /// el motor ordena por CantidadMínima descendente y coge la primera, NO la del % mayor.
        /// Con el criterio viejo la tienda anunciaba un 30 % que un pedido de una unidad no
        /// aplicaba.
        /// </summary>
        [TestMethod]
        public void DosFilasDelMismoProducto_GanaLaDeMayorCantidadMinima_NoLaDelPorcentajeMayor()
        {
            DescuentosPorAudiencia resultado = Calcular(
                DeProducto(0.30M, cantidadMinima: 0),
                DeProducto(0.12M, cantidadMinima: 1));

            Assert.AreEqual(12M, resultado.Profesional);
        }

        /// <summary>
        /// El motor nunca lee el Precio de una fila de familia (sus niveles solo miran Descuento),
        /// y repartir un precio fijo entre las referencias de una marca no significaría nada: un
        /// bote de 200 € y uno de 8 € no valen los mismos 5,45.
        /// </summary>
        [TestMethod]
        public void PrecioFijoEnFilaDeFamilia_NoSeDerivaAPorcentaje()
        {
            DescuentosPorAudiencia resultado = Calcular(DeFamilia(0M, precio: 60M));

            Assert.IsNull(resultado.Profesional);
        }

        // En el nivel de producto sí se deriva, como hacía el paso 7 del proceso legacy.
        [TestMethod]
        public void PrecioFijoEnFilaDeProducto_SeDerivaContraElPvp()
        {
            DescuentosPorAudiencia resultado = Calcular(DeProducto(0M, precio: 60M));

            Assert.AreEqual(40M, resultado.Profesional);
        }

        // La audiencia se sigue respetando nivel a nivel: una campaña de marca solo para
        // profesionales no la ve el público.
        [TestMethod]
        public void FamiliaSoloProfesional_ElPublicoNoSeEntera()
        {
            DescuentosPorAudiencia resultado = Calcular(DeFamilia(0.15M, ambito: 1));

            Assert.AreEqual(15M, resultado.Profesional);
            Assert.IsNull(resultado.Publico);
        }

        // Sin grupo en la ficha, una fila de familia+grupo no puede alcanzar al producto.
        [TestMethod]
        public void SinGrupoEnLaFicha_LaFilaDeFamiliaYGrupoNoSeAplica()
        {
            DescuentosPorAudiencia resultado = ProductoDTO.CalcularDescuentosPorAudiencia(
                new[] { DeFamilia(0.20M), DeFamiliaYGrupo(0.10M) }, 100M, null);

            Assert.AreEqual(20M, resultado.Profesional);
        }
    }
}
