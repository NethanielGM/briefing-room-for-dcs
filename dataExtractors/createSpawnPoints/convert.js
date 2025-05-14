const distance = (p1, p2) => Math.hypot(p2[0] - p1[0], p2[1] - p1[1]);

function filterClosePoints(points, threshold) {
    const uniquePoints = [];
    points.forEach(point => {
        if (uniquePoints.length === 0 || distance(point.coords, uniquePoints[uniquePoints.length - 1].coords) > threshold) {
            uniquePoints.push(point);
        }
    });
    return uniquePoints;
}


const fs = require('fs');
const map = process.argv[2];
const original = JSON.parse(fs.readFileSync(`../../DatabaseJSON/TheaterSpawnPoints/${map}.json`, 'utf8'))
const spawnPoints = JSON.parse(fs.readFileSync(`../../SpawnPoints.json`, 'utf8'))

console.log("Original: ", original.length)
console.log("New: ", spawnPoints.length)

const filtered = filterClosePoints(spawnPoints, 251)
console.log("Filtered: ", filtered.length)


const complete = original.concat(filtered)
console.log("Complete: ", complete.length)

complete.forEach((sp, i) => {
    sp.coords = [sp.coords[0].toFixed(2), sp.coords[1].toFixed(2)]
})

fs.writeFileSync(`../../DatabaseJSON/TheaterSpawnPoints/${map}.json`, JSON.stringify(complete, null, 2), 'utf8')