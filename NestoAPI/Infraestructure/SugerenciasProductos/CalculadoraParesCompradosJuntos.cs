using ModeloSugerenciasProductos.Reglas;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>
    /// NestoAPI#670: lo que calcula de noche el job «sugerencias-productos-pares», sin BD.
    /// <para><b>Pares</b> (reglas de asociación): una cesta es lo que se ha llevado un cliente en un día (albaranes de 24 meses).
    /// Por cada pareja A→B: VecesJuntos (cestas con los dos), Soporte (VecesJuntos ÷ cestas), Confianza (VecesJuntos ÷ cestas
    /// con A) y Lift (Confianza ÷ proporción de cestas con B). Se queda con los pares que se repiten (al menos
    /// <see cref="MINIMO_VECES_JUNTOS"/> cestas), con confianza y lift mínimos, y con los <see cref="MAXIMO_PARES_POR_PRODUCTO"/>
    /// mejores de cada A. Las cestas de más de <see cref="MAXIMO_PRODUCTOS_CESTA"/> productos (aperturas, reposiciones de una
    /// tienda) no cuentan: meterían ruido y n² parejas.</para>
    /// <para><b>Perfiles</b>: el perfil de cada cliente (<see cref="MotorSugerenciasProductos.Perfil"/>) y, por perfil, cuántos
    /// clientes se han llevado cada producto en <see cref="MESES_PERFIL"/> meses (los <see cref="MAXIMO_PRODUCTOS_POR_PERFIL"/>
    /// primeros, con al menos <see cref="MINIMO_CLIENTES_PERFIL"/> clientes).</para>
    /// <para>NestoAPI#671 (corte 2b): las cuentas viven en el núcleo (<see cref="TablasNocturnas"/> y <see cref="ReglasSugerencias"/>),
    /// las mismas que usa el entrenamiento offline con lo anterior a cada instantánea; aquí solo se adaptan los tipos.</para>
    /// </summary>
    public static class CalculadoraParesCompradosJuntos
    {
        public const int MINIMO_VECES_JUNTOS = TablasNocturnas.MINIMO_VECES_JUNTOS;
        public const decimal MINIMO_CONFIANZA = TablasNocturnas.MINIMO_CONFIANZA;
        public const decimal MINIMO_LIFT = TablasNocturnas.MINIMO_LIFT;
        public const int MAXIMO_PARES_POR_PRODUCTO = TablasNocturnas.MAXIMO_PARES_POR_PRODUCTO;
        public const int MAXIMO_PRODUCTOS_CESTA = TablasNocturnas.MAXIMO_PRODUCTOS_CESTA;
        public const int MESES_PERFIL = ReglasSugerencias.MESES_PERFIL;
        public const int MINIMO_CLIENTES_PERFIL = ReglasSugerencias.MINIMO_CLIENTES_PERFIL;
        public const int MAXIMO_PRODUCTOS_POR_PERFIL = ReglasSugerencias.MAXIMO_PRODUCTOS_POR_PERFIL;

        public static List<ParCompradoJunto> Pares(IEnumerable<CompraDiaReposicion> compras, IDictionary<string, FichaProductoSugerencia> fichas)
        {
            var buscar = MotorSugerenciasProductos.Buscador(fichas);
            return TablasNocturnas.Pares(MotorSugerenciasProductos.ACompras(compras), p => ReglasSugerencias.EsSugerible(buscar(p)))
                .Select(p => new ParCompradoJunto
                {
                    ProductoA = p.ProductoA,
                    ProductoB = p.ProductoB,
                    VecesJuntos = p.VecesJuntos,
                    Soporte = p.Soporte,
                    Confianza = p.Confianza,
                    Lift = p.Lift
                })
                .ToList();
        }

        public static List<ProductoPerfil> PerfilesProductos(IEnumerable<CompraDiaReposicion> compras,
            IDictionary<string, FichaProductoSugerencia> fichas, System.DateTime hoy)
        {
            if (fichas == null)
            {
                return new List<ProductoPerfil>();
            }
            return ReglasSugerencias.PerfilesProductos(MotorSugerenciasProductos.ACompras(compras), MotorSugerenciasProductos.Buscador(fichas), hoy)
                .Select(p => new ProductoPerfil { Grupo = p.Grupo, SubGrupo = p.SubGrupo, Producto = p.Producto, Clientes = p.Clientes })
                .ToList();
        }
    }
}
