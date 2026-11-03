-- 1. Top five customers by spend in the last 30 days.
SELECT TOP (5)
    u.Id AS CustomerId,
    u.FullName,
    u.Email,
    SUM(o.Total) AS TotalSpend
FROM dbo.Orders AS o
INNER JOIN dbo.Users AS u ON u.Id = o.CustomerId
WHERE o.CreatedAt >= DATEADD(DAY, -30, SYSUTCDATETIME())
  AND o.Status IN (1, 2) -- Confirmed, Shipped
GROUP BY u.Id, u.FullName, u.Email
ORDER BY SUM(o.Total) DESC, u.Id;

-- 2. Products that have never been included in an order.
SELECT p.Id, p.Sku, p.Name, p.Price, p.StockQuantity
FROM dbo.Products AS p
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.OrderItems AS oi
    WHERE oi.ProductId = p.Id
);

-- 3. Every month in the current year, revenue and year-to-date running revenue.
;WITH Months AS
(
    SELECT MonthNumber
    FROM (VALUES (1), (2), (3), (4), (5), (6), (7), (8), (9), (10), (11), (12)) AS m(MonthNumber)
),
MonthlyRevenue AS
(
    SELECT MONTH(o.CreatedAt) AS MonthNumber, SUM(o.Total) AS Revenue
    FROM dbo.Orders AS o
    WHERE o.CreatedAt >= DATEFROMPARTS(YEAR(SYSUTCDATETIME()), 1, 1)
      AND o.CreatedAt < DATEFROMPARTS(YEAR(SYSUTCDATETIME()) + 1, 1, 1)
      AND o.Status IN (1, 2)
    GROUP BY MONTH(o.CreatedAt)
)
SELECT
    YEAR(SYSUTCDATETIME()) AS RevenueYear,
    m.MonthNumber,
    COALESCE(r.Revenue, 0) AS Revenue,
    SUM(COALESCE(r.Revenue, 0)) OVER (ORDER BY m.MonthNumber ROWS UNBOUNDED PRECEDING) AS RunningRevenue
FROM Months AS m
LEFT JOIN MonthlyRevenue AS r ON r.MonthNumber = m.MonthNumber
ORDER BY m.MonthNumber;

-- 4. Each customer's second-highest distinct order total.
;WITH RankedOrders AS
(
    SELECT
        o.CustomerId,
        o.Total,
        DENSE_RANK() OVER (PARTITION BY o.CustomerId ORDER BY o.Total DESC) AS TotalRank
    FROM dbo.Orders AS o
)
SELECT CustomerId, Total AS SecondHighestOrderTotal
FROM RankedOrders
WHERE TotalRank = 2
ORDER BY CustomerId;

-- 5. Products with stock below their average units sold per order line.
SELECT p.Id, p.Sku, p.Name, p.StockQuantity, AVG(CAST(oi.Quantity AS decimal(18, 2))) AS AverageQuantityPerOrder
FROM dbo.Products AS p
INNER JOIN dbo.OrderItems AS oi ON oi.ProductId = p.Id
GROUP BY p.Id, p.Sku, p.Name, p.StockQuantity
HAVING p.StockQuantity < AVG(CAST(oi.Quantity AS decimal(18, 2)))
ORDER BY p.Id;
