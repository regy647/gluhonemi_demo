del obj\Docker\publish\*.* /F /Q

echo "Building app..."
dotnet clean -c Release 
dotnet restore 
dotnet build -c Release 
dotnet publish -c Release -o obj/Docker/publish

echo "🐳 Building Docker image..."
docker build --platform linux/amd64 -t soniox .

echo "🏷️ Tagging image for registry..."
docker tag soniox:latest 207.154.202.182:5000/soniox:latest

echo "📤 Pushing image to registry..."
docker push 207.154.202.182:5000/soniox:latest

echo "✅ Deployment complete."