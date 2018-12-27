﻿namespace NestoAPI.Models.Comisiones
{
    public class CalculadorProyecciones2018 : ICalculadorProyecciones
    {
        public decimal CalcularProyeccion(string vendedor, int anno, int mes, decimal ventaAcumulada, int meses, int mesesAnno)
        {
            return (ventaAcumulada / meses) * mesesAnno;
        }
    }
}