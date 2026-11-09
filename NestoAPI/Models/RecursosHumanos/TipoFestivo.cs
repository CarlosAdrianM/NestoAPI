namespace NestoAPI.Models.RecursosHumanos
{
    public enum TipoFestivo
    {
        Empresa,
        Nacional,
        Autonómica,
        Alcobendas,
        Algete,
        Madrid,
        /// <summary>
        /// NestoAPI#841 (Carlos 09/11/26): «día sin reparto propio» (no sale la furgoneta: el repartidor libra). No es festivo de
        /// ningún almacén ni tienda (Algete, Reina y Alcobendas trabajan): solo frena nuestras rutas (AT/16, agencia 14) y las
        /// reposiciones de tiendas, que lleva la furgoneta. Ver <see cref="GestorFestivos.EsDiaSinRepartoPropio"/>.
        /// </summary>
        RepartoPropio
    }
}